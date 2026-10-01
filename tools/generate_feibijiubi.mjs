import crypto from "node:crypto";
import fs from "node:fs/promises";
import path from "node:path";
import { spawn } from "node:child_process";
import { fileURLToPath } from "node:url";
import { actions as allActions, originalSha256, prompts } from "./feibijiubi_prompts.mjs";

const projectRoot = fileURLToPath(new URL("../", import.meta.url));
const characterRoot = path.join(projectRoot, "assets", "characters", "feibijiubi");
const model = "gpt-image-2";
const cookieName = "image_edit_web_session";
const sha256 = (bytes) => crypto.createHash("sha256").update(bytes).digest("hex");
const relative = (file) => path.relative(characterRoot, file).split(path.sep).join("/");

function parseOptions(args) {
  const options = { actions: allActions, concurrency: 2, dryRun: false, directCli: false, variant: "" };
  for (let index = 0; index < args.length; index += 1) {
    const flag = args[index];
    if (flag === "--dry-run") options.dryRun = true;
    else if (flag === "--direct-cli") options.directCli = true;
    else if (flag === "--help") options.help = true;
    else if (flag === "--actions") options.actions = [...new Set((args[++index] ?? "").split(",").filter(Boolean))];
    else if (flag === "--concurrency") options.concurrency = Number(args[++index]);
    else if (flag === "--variant") options.variant = args[++index] ?? "";
    else throw new Error("Unknown option. Use --help for the supported arguments.");
  }
  if (!options.actions.length || options.actions.some((action) => !allActions.includes(action))) {
    throw new Error("Actions must be a comma-separated subset of the 17 supported animation names.");
  }
  if (![1, 2, 3].includes(options.concurrency)) throw new Error("Concurrency must be 1, 2 or 3.");
  if (options.variant && !/^[a-z0-9][a-z0-9-]{0,39}$/.test(options.variant)) {
    throw new Error("Variant must contain only lowercase letters, numbers and hyphens.");
  }
  return options;
}

async function readJson(file) {
  try {
    return JSON.parse(await fs.readFile(file, "utf8"));
  } catch (error) {
    if (error.code === "ENOENT") return null;
    throw new Error("A generation receipt could not be read. Preserve it and use a new --variant.");
  }
}

async function exists(file) {
  try { await fs.access(file); return true; }
  catch (error) {
    if (error.code === "ENOENT") return false;
    throw new Error("The output directory could not be accessed.");
  }
}

async function writeJson(file, value) {
  const temporary = `${file}.pending`;
  await fs.writeFile(temporary, `${JSON.stringify(value, null, 2)}\n`, "utf8");
  await fs.rename(temporary, file);
}

function getApiBase() {
  const base = new URL(process.env.IMAGE_EDIT_BASE || "http://127.0.0.1:3100");
  const loopback = ["localhost", "127.0.0.1", "[::1]"].includes(base.hostname);
  if ((!loopback && base.protocol !== "https:") || !["http:", "https:"].includes(base.protocol)
      || base.username || base.password || base.search || base.hash || base.pathname !== "/") {
    throw new Error("IMAGE_EDIT_BASE must be an HTTPS origin or a local loopback HTTP origin without embedded credentials.");
  }
  return base.origin;
}

async function request(url, options, label) {
  try {
    return await fetch(url, { ...options, redirect: "error" });
  } catch {
    throw new Error(`${label} could not connect or was interrupted; completed actions remain saved.`);
  }
}

async function authenticate(apiBase) {
  const sessionCookie = process.env.IMAGE_EDIT_SESSION_COOKIE?.trim();
  if (sessionCookie) {
    if (!new RegExp(`^${cookieName}=[A-Za-z0-9_\\-%.]+$`).test(sessionCookie)) {
      throw new Error("IMAGE_EDIT_SESSION_COOKIE must contain only the existing image_edit_web_session cookie name and value.");
    }
    return sessionCookie;
  }
  const username = process.env.IMAGE_EDIT_USERNAME;
  const password = process.env.IMAGE_EDIT_PASSWORD;
  if (!username || !password) {
    throw new Error("Set IMAGE_EDIT_USERNAME and IMAGE_EDIT_PASSWORD locally, or pass an existing authorized session through IMAGE_EDIT_SESSION_COOKIE. Do not put credentials in project files.");
  }
  const response = await request(`${apiBase}/api/auth/login`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ username, password }),
  }, "Login");
  if (!response.ok) throw new Error(`Normal login failed (HTTP ${response.status}).`);
  const cookies = response.headers.getSetCookie?.() ?? [response.headers.get("set-cookie") || ""];
  const cookie = cookies.map((value) => value.split(";", 1)[0]).find((value) => value.startsWith(`${cookieName}=`));
  if (!cookie) throw new Error("Normal login returned no usable session cookie.");
  return cookie;
}

function imageDimensions(bytes) {
  if (bytes.length < 24 || !bytes.subarray(0, 8).equals(Buffer.from([137, 80, 78, 71, 13, 10, 26, 10]))) {
    throw new Error("The service returned an invalid PNG; no output was accepted.");
  }
  return [bytes.readUInt32BE(16), bytes.readUInt32BE(20)];
}

function getProviderOrigin() {
  let provider;
  try { provider = new URL(process.env.OPENAI_BASE_URL || "https://api.openai.com/v1"); }
  catch { throw new Error("OPENAI_BASE_URL must be a valid HTTPS API base URL."); }
  if (provider.protocol !== "https:" || provider.username || provider.password || provider.search || provider.hash) {
    throw new Error("OPENAI_BASE_URL must use HTTPS without embedded credentials or query parameters.");
  }
  return provider.origin;
}

async function generateWithCli(item, imagePath) {
  const python = process.env.IMAGE_GEN_PYTHON?.trim() || "python";
  const cli = process.env.IMAGE_GEN_CLI?.trim();
  if (!cli) throw new Error("Set IMAGE_GEN_CLI to the imagegen CLI script path before using --direct-cli.");
  const args = [
    cli, "edit", "--model", model,
    "--image", path.join(characterRoot, "original.png"),
    "--prompt-file", path.join(characterRoot, item.prompt),
    "--size", "2048x2048", "--quality", "high", "--output-format", "png",
    "--no-augment", "--out", imagePath,
  ];
  const childEnvironment = {};
  for (const key of [
    "SystemRoot", "WINDIR", "PATH", "PATHEXT", "TEMP", "TMP", "USERPROFILE",
    "APPDATA", "LOCALAPPDATA", "VIRTUAL_ENV", "SSL_CERT_FILE", "SSL_CERT_DIR",
    "HTTPS_PROXY", "HTTP_PROXY", "NO_PROXY", "OPENAI_API_KEY", "OPENAI_BASE_URL",
  ]) {
    if (process.env[key] !== undefined) childEnvironment[key] = process.env[key];
  }
  await new Promise((resolve, reject) => {
    const child = spawn(python, args, {
      cwd: projectRoot, env: childEnvironment, windowsHide: true,
      shell: false, stdio: ["ignore", "pipe", "pipe"],
    });
    // Provider diagnostics can contain credentials; drain them without printing or persisting.
    child.stdout.resume();
    child.stderr.resume();
    child.once("error", () => reject(new Error("CLI exit code unavailable (process could not start).")));
    child.once("close", (code) => {
      if (code === 0) resolve();
      else reject(new Error(`CLI exit code ${code ?? "unavailable"}.`));
    });
  });
}

async function main() {
  const options = parseOptions(process.argv.slice(2));
  if (options.help) {
    process.stdout.write("Usage: node tools/generate_feibijiubi.mjs [--actions idle,run,chomp] [--concurrency 2] [--direct-cli] [--dry-run] [--variant v2]\n");
    process.stdout.write(`Actions: ${allActions.join(",")}\n`);
    return;
  }

  const original = await fs.readFile(path.join(characterRoot, "original.png"));
  if (sha256(original) !== originalSha256) {
    throw new Error("Original reference SHA-256 mismatch. Generated sprites must never be used as model inputs.");
  }
  const outputRoot = path.join(characterRoot, options.variant ? `source-api-${options.variant}` : "source-api");
  const planRoot = path.join(outputRoot, "plans");
  await fs.mkdir(planRoot, { recursive: true });
  const pathsFor = (action) => ({
    image: path.join(outputRoot, `${action}_sheet_api.png`),
    prompt: path.join(outputRoot, `${action}_prompt.txt`),
    receipt: path.join(outputRoot, `${action}_generation.json`),
  });

  const plan = {
    character: "feibijiubi", model, size: [2048, 2048], grid: [4, 4], frameCount: 16,
    transport: options.directCli ? "direct-cli" : "image-edit-web",
    sourceImage: "original.png", sourceSha256: originalSha256,
    lineagePolicy: "original-image-only; generated images are never model inputs",
    concurrency: options.concurrency,
    actions: [],
  };
  for (const action of options.actions) {
    const promptSha256 = sha256(prompts[action]);
    const promptFile = path.join(planRoot, `${action}_${promptSha256}.txt`);
    await fs.writeFile(promptFile, prompts[action], "utf8");
    plan.actions.push({ action, prompt: relative(promptFile), promptSha256 });
  }
  await writeJson(path.join(outputRoot, "generation-plan.json"), plan);
  if (options.dryRun) {
    process.stdout.write(JSON.stringify({ dryRun: true, actions: options.actions, plan: relative(path.join(outputRoot, "generation-plan.json")) }) + "\n");
    return;
  }

  const completed = new Map();
  for (const action of allActions) {
    const files = pathsFor(action);
    const receipt = await readJson(files.receipt);
    if (receipt) completed.set(action, receipt);
  }
  const queue = [];
  for (const item of plan.actions) {
    const files = pathsFor(item.action);
    const receipt = completed.get(item.action);
    if (receipt && await exists(files.image) && await exists(files.prompt)
        && receipt.sourceSha256 === originalSha256 && receipt.promptSha256 === item.promptSha256
        && receipt.outputSha256 === sha256(await fs.readFile(files.image))
        && sha256(await fs.readFile(files.prompt)) === item.promptSha256) {
      process.stdout.write(`Already complete: ${item.action}\n`);
      continue;
    }
    if (receipt || await exists(files.image) || await exists(files.prompt)) {
      throw new Error(`${item.action} has existing or changed output. Preserve it and use --variant with a new name to regenerate from the original.`);
    }
    queue.push(item);
  }
  if (!queue.length) {
    await writeJson(path.join(outputRoot, "generation-manifest.json"), {
      character: "feibijiubi", model, sourceImage: "original.png", sourceSha256: originalSha256,
      lineagePolicy: plan.lineagePolicy,
      results: [...completed.values()].sort((left, right) => left.action.localeCompare(right.action)),
    });
    process.stdout.write("All requested actions are already complete and their hashes match.\n");
    return;
  }

  const apiBase = options.directCli ? null : getApiBase();
  const providerOrigin = options.directCli ? getProviderOrigin() : apiBase;
  if (options.directCli && !process.env.OPENAI_API_KEY) {
    throw new Error("Direct CLI requires OPENAI_API_KEY in the inherited environment; do not put credentials in project files.");
  }
  const cookie = options.directCli ? null : await authenticate(apiBase);
  let manifestWrite = Promise.resolve();
  const failures = [];
  async function generate(item) {
    process.stdout.write(`Generating: ${item.action}\n`);
    const files = pathsFor(item.action);
    const bytes = options.directCli
      ? await (async () => { await generateWithCli(item, files.image); return fs.readFile(files.image); })()
      : await generateWithWeb(item);
    const size = imageDimensions(bytes);
    if (size[0] !== 2048 || size[1] !== 2048) throw new Error("Image dimensions do not match the required 2048-square sheet.");
    const receipt = {
      action: item.action, model, generatedAt: new Date().toISOString(),
      transport: plan.transport, providerOrigin,
      sourceImage: "original.png", sourceSha256: originalSha256,
      prompt: relative(files.prompt), promptSha256: item.promptSha256,
      image: relative(files.image), outputSha256: sha256(bytes), size, grid: [4, 4], frameCount: 16,
      lineagePolicy: plan.lineagePolicy,
    };
    if (!options.directCli) await fs.writeFile(files.image, bytes, { flag: "wx" });
    await fs.writeFile(files.prompt, prompts[item.action], { flag: "wx" });
    await writeJson(files.receipt, receipt);
    completed.set(item.action, receipt);
    manifestWrite = manifestWrite.then(() => writeJson(path.join(outputRoot, "generation-manifest.json"), {
      character: "feibijiubi", model, sourceImage: "original.png", sourceSha256: originalSha256,
      lineagePolicy: plan.lineagePolicy,
      results: [...completed.values()].sort((left, right) => left.action.localeCompare(right.action)),
    }));
    await manifestWrite;
    process.stdout.write(`Saved: ${item.action}\n`);
  }

  async function generateWithWeb(item) {
    const form = new FormData();
    form.append("images", new Blob([original], { type: "image/png" }), "original-character-reference.png");
    const fields = {
      prompt: prompts[item.action], imageModel: model, apiProvider: "auto", aspectRatio: "1:1",
      resolution: "2k", quality: "high", background: "opaque", outputFormat: "png",
      n: "1", preset: "none", stylePreset: "none",
    };
    for (const [key, value] of Object.entries(fields)) form.append(key, value);
    const response = await request(`${apiBase}/api/edit`, {
      method: "POST", headers: { Cookie: cookie, "X-API-Provider": "auto" }, body: form,
    }, item.action);
    if (!response.ok) throw new Error(`Generation failed (HTTP ${response.status}).`);
    let result;
    try { result = await response.json(); }
    catch { throw new Error("Generation returned invalid JSON."); }
    if (result.model !== model || !result.images?.[0]?.url) {
      throw new Error("Generation returned an unexpected model or no image.");
    }
    const imageUrl = new URL(result.images[0].url, apiBase);
    if (imageUrl.origin !== apiBase || imageUrl.username || imageUrl.password) {
      throw new Error("Image download must remain on the authenticated service origin.");
    }
    const download = await request(imageUrl, { headers: { Cookie: cookie } }, `${item.action} download`);
    if (!download.ok) throw new Error(`Image download failed (HTTP ${download.status}).`);
    return Buffer.from(await download.arrayBuffer());
  }

  async function worker() {
    while (queue.length) {
      const item = queue.shift();
      try { await generate(item); }
      catch (error) {
        failures.push(item.action);
        const safeMessage = error.code ? "Local output could not be saved; preserve existing files before retrying." : error.message;
        process.stderr.write(`${item.action}: ${safeMessage}\n`);
      }
    }
  }
  await Promise.all(Array.from({ length: options.concurrency }, () => worker()));
  await manifestWrite;
  process.stdout.write(JSON.stringify({ completed: [...completed.keys()].sort(), failed: failures }) + "\n");
  if (failures.length) process.exitCode = 1;
}

main().catch((error) => {
  process.stderr.write(`${error.code ? "Local source or output could not be accessed; no credentials were recorded." : error.message}\n`);
  process.exitCode = 1;
});
