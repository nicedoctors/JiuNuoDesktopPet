import crypto from "node:crypto";
import fs from "node:fs/promises";
import path from "node:path";
import { spawn } from "node:child_process";
import { fileURLToPath } from "node:url";
import { actions, originalSha256, prompts } from "./feibijiubi_prank_prompts.mjs";

// Project-owned orchestration of the bundled imagegen CLI; no alternate image SDK.
const root = fileURLToPath(new URL("../assets/characters/feibijiubi/", import.meta.url));
const digest = bytes => crypto.createHash("sha256").update(bytes).digest("hex");
const rel = file => path.relative(root, file).split(path.sep).join("/");
const args = process.argv.slice(2);
const option = (name, fallback) => args.includes(name) ? args[args.indexOf(name) + 1] : fallback;
const selected = option("--actions", actions.join(",")).split(",");
const variant = option("--variant", "v1");
const concurrency = Number(option("--concurrency", "2"));
const providerName = option("--provider", "primary");
if (!["primary", "official", "ucloud-pool"].includes(providerName)) throw new Error("Provider must be primary, official or ucloud-pool.");
const poolSlots = option("--pool-slots", "2,3").split(",").map(Number);
if (!selected.length || selected.some(x => !actions.includes(x)) || !/^[a-z0-9-]{1,40}$/.test(variant) || ![1, 2, 3].includes(concurrency)) throw new Error("Invalid action, variant or concurrency.");
const output = path.join(root, `source-pranks-${variant}`);
const dry = args.includes("--dry-run");
const json = async (file, value) => fs.writeFile(file, `${JSON.stringify(value, null, 2)}\n`);
const read = async file => { try { return JSON.parse(await fs.readFile(file, "utf8")); } catch (error) { if (error.code === "ENOENT") return null; throw new Error("Receipt unreadable; preserve output and use a new variant."); } };
const exists = async file => { try { await fs.access(file); return true; } catch { return false; } };

async function main() {
  if (digest(await fs.readFile(path.join(root, "original.png"))) !== originalSha256) throw new Error("Original SHA mismatch; generated images must never be model inputs.");
  await fs.mkdir(path.join(output, "plans"), { recursive: true });
  const queue = [];
  for (const action of selected) {
    const promptSha256 = digest(prompts[action]);
    const planPrompt = path.join(output, "plans", `${action}_${promptSha256}.txt`);
    await fs.writeFile(planPrompt, prompts[action]);
    const image = path.join(output, `${action}_sheet_api.png`);
    const prompt = path.join(output, `${action}_prompt.txt`);
    const receiptPath = path.join(output, `${action}_generation.json`);
    const receipt = await read(receiptPath);
    if (receipt && receipt.sourceSha256 === originalSha256 && receipt.promptSha256 === promptSha256 && await exists(image) && digest(await fs.readFile(image)) === receipt.outputSha256 && await exists(prompt) && digest(await fs.readFile(prompt)) === promptSha256) { process.stdout.write(`Already complete: ${action}\n`); continue; }
    if (receipt || await exists(image) || await exists(prompt)) throw new Error(`${action}: existing output differs; use a new variant from original.`);
    queue.push({ action, promptSha256, planPrompt, image, prompt, receiptPath });
  }
  if (dry) { process.stdout.write(JSON.stringify({ actions: selected, pending: queue.map(x => x.action), originalSha256, output: rel(output) }) + "\n"); return; }
  const python = process.env.IMAGE_GEN_PYTHON?.trim() || "python";
  const cli = process.env.IMAGE_GEN_CLI?.trim();
  if (!cli) throw new Error("Set IMAGE_GEN_CLI to the imagegen CLI script path before generating images.");
  const configuredPool = [...new Set([process.env.OPENAI_API_KEY, ...String(process.env.UCLOUD_API_KEYS || "").split(/[,;\r\n]+/)].map(value => String(value || "").trim()).filter(Boolean))];
  if (providerName === "ucloud-pool" && (concurrency > poolSlots.length || new Set(poolSlots).size !== poolSlots.length || poolSlots.some(slot => !Number.isInteger(slot) || slot < 2 || slot > configuredPool.length))) throw new Error("Choose distinct existing pool slots (2 or higher); the depleted primary slot stays disabled.");
  const apiKey = providerName === "official" ? process.env.OPENAI_FALLBACK_API_KEY : process.env.OPENAI_API_KEY;
  const apiBase = providerName === "official" ? process.env.OPENAI_FALLBACK_BASE_URL || "https://api.openai.com/v1" : process.env.OPENAI_BASE_URL || "https://api.openai.com/v1";
  if (!apiKey) throw new Error("Configured API key is unavailable; no credentials recorded.");
  const provider = new URL(apiBase);
  if (provider.protocol !== "https:" || provider.username || provider.password || provider.search || provider.hash) throw new Error("Provider must be credential-free HTTPS origin.");
  const childEnvironment = {};
  for (const key of ["SystemRoot", "WINDIR", "PATH", "PATHEXT", "TEMP", "TMP", "USERPROFILE", "APPDATA", "LOCALAPPDATA", "SSL_CERT_FILE", "SSL_CERT_DIR", "HTTPS_PROXY", "HTTP_PROXY", "NO_PROXY", "OPENAI_API_KEY", "OPENAI_BASE_URL"]) if (process.env[key] !== undefined) childEnvironment[key] = process.env[key];
  childEnvironment.OPENAI_API_KEY = apiKey;
  childEnvironment.OPENAI_BASE_URL = apiBase;
  // Match image_edit_web's existing provider proxy without changing OS settings.
  if (process.env.OPENAI_PROXY) childEnvironment.HTTPS_PROXY = process.env.OPENAI_PROXY;
  const failed = [];
  let stopReason = null;
  async function worker(workerIndex) {
    const workerEnvironment = { ...childEnvironment };
    const providerSlot = providerName === "ucloud-pool" ? poolSlots[workerIndex] : null;
    if (providerSlot) workerEnvironment.OPENAI_API_KEY = configuredPool[providerSlot - 1];
    while (queue.length && !stopReason) {
      const item = queue.shift();
      process.stdout.write(`Generating: ${item.action}\n`);
      try {
        await new Promise((resolve, reject) => {
          const child = spawn(python, [cli, "edit", "--model", "gpt-image-2", "--image", path.join(root, "original.png"), "--prompt-file", item.planPrompt, "--size", "2048x2048", "--quality", "high", "--output-format", "png", "--no-augment", "--out", item.image], { env: workerEnvironment, windowsHide: true, shell: false, stdio: ["ignore", "pipe", "pipe"] });
          let diagnostics = "";
          child.stdout.resume();
          child.stderr.on("data", chunk => { diagnostics = (diagnostics + chunk.toString()).slice(-24000); });
          child.once("error", () => reject(new Error("Image CLI could not start.")));
          child.once("close", code => {
            if (code === 0) { resolve(); return; }
            if (/quota exceeded|insufficient_quota|daily_limit_amount/i.test(diagnostics)) {
              stopReason = "provider-quota-exceeded";
              reject(new Error("Provider quota exceeded; remaining requests stopped. No fallback key used."));
            } else if (/APIConnectionError/.test(diagnostics)) reject(new Error("Provider connection failed."));
            else reject(new Error(`Image CLI exit ${code}.`));
          });
        });
        const bytes = await fs.readFile(item.image);
        if (bytes.readUInt32BE(16) !== 2048 || bytes.readUInt32BE(20) !== 2048) throw new Error("Unexpected image dimensions.");
        await fs.writeFile(item.prompt, prompts[item.action], { flag: "wx" });
        await json(item.receiptPath, { action: item.action, model: "gpt-image-2", transport: "bundled-imagegen-cli", provider: providerName, providerSlot, providerOrigin: provider.origin, sourceImage: "original.png", sourceSha256: originalSha256, prompt: rel(item.prompt), promptSha256: item.promptSha256, image: rel(item.image), outputSha256: digest(bytes), size: [2048, 2048], grid: [4, 4], frameCount: 16, generatedAt: new Date().toISOString(), lineagePolicy: "original-image-only; generated images never model inputs" });
        process.stdout.write(`Saved: ${item.action}\n`);
      } catch (error) { failed.push(item.action); process.stderr.write(`${item.action}: ${error.code ? "Local file operation failed." : error.message}\n`); }
    }
  }
  await Promise.all(Array.from({ length: concurrency }, (_, index) => worker(index)));
  const receipts = [];
  for (const action of actions) { const receipt = await read(path.join(output, `${action}_generation.json`)); if (receipt) receipts.push(receipt); }
  await json(path.join(output, "generation-manifest.json"), { originalSha256, actions: receipts });
  await json(path.join(output, `generation-status-${providerName}.json`), { updatedAt: new Date().toISOString(), provider: providerName, successfulSheetCount: receipts.length, failedActions: failed, pendingActions: queue.map(x => x.action), stopReason, automaticProviderFallback: false });
  process.stdout.write(JSON.stringify({ completed: receipts.map(x => x.action), failed, pending: queue.map(x => x.action), stopReason }) + "\n");
  if (failed.length) process.exitCode = 1;
}
main().catch(error => { process.stderr.write(`${error.code ? "Local source or output unavailable; no credentials recorded." : error.message}\n`); process.exitCode = 1; });
