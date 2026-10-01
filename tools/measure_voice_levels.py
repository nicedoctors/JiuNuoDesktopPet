"""Measure packaged WAV loudness offline; never opens an audio output device."""
import concurrent.futures
import hashlib
import json
import math
from pathlib import Path
import shutil
import statistics
import subprocess

ROOT = Path(__file__).resolve().parents[1]
FFMPEG = shutil.which("ffmpeg")
OUT = ROOT / "assets/audio/voice-levels.json"

def measure(spec):
    character, file = spec
    result = subprocess.run([FFMPEG, "-hide_banner", "-nostdin", "-i", str(file),
        "-af", "loudnorm=I=-24:TP=-2:LRA=11:print_format=json", "-f", "null", "-"],
        capture_output=True, text=True, encoding="utf-8", errors="replace",
        creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0), check=True)
    start = result.stderr.rfind("{")
    data, _ = json.JSONDecoder().raw_decode(result.stderr[start:])
    loudness, peak = float(data["input_i"]), float(data["input_tp"])
    if not math.isfinite(loudness) or not math.isfinite(peak):
        raise ValueError(f"Unmeasurable recording: {file.name}")
    return {"Character": character, "File": file.name,
            "Sha256": hashlib.sha256(file.read_bytes()).hexdigest(),
            "IntegratedLufs": loudness, "TruePeakDbtp": peak}

def main():
    if not FFMPEG:
        raise RuntimeError("FFmpeg is required only for offline asset measurement.")
    sources = [("nuonuo", f) for f in sorted((ROOT/"音效").glob("*.wav"))]
    sources += [("feibijiubi", f) for f in sorted((ROOT/"assets/characters/feibijiubi/audio").glob("*.wav"))]
    with concurrent.futures.ThreadPoolExecutor(max_workers=4) as pool:
        records = list(pool.map(measure, sources))
    # Attenuation only: retain the quieter character's ordinary voice level.
    reference = statistics.median(r["IntegratedLufs"] for r in records if r["Character"] == "nuonuo")
    target = min(-24, math.floor(reference))
    for r in records:
        quiet = any(word in r["File"] for word in ["小声","轻声","悄悄话","困倦"])
        desired = target - (3 if quiet else 0)
        gain = min(0, desired-r["IntegratedLufs"], -2-r["TruePeakDbtp"])
        r["GainDb"] = round(gain, 3)
        r["Gain"] = round(10**(gain/20), 8)
        r["BalancedLufs"] = round(r["IntegratedLufs"]+gain, 3)
    version = subprocess.run([FFMPEG,"-version"], capture_output=True,text=True,
        creationflags=getattr(subprocess,"CREATE_NO_WINDOW",0),check=True).stdout.splitlines()[0]
    OUT.parent.mkdir(parents=True,exist_ok=True)
    OUT.write_text(json.dumps({"TargetLufs":target,"Tool":version,"Clips":records},
        ensure_ascii=False,indent=2)+"\n",encoding="utf-8")
    for character in ["nuonuo","feibijiubi"]:
        rows=[r for r in records if r["Character"]==character]
        print(character, "files",len(rows),"median LUFS",statistics.median(r["IntegratedLufs"] for r in rows),
              "balanced",statistics.median(r["BalancedLufs"] for r in rows),"target",target)

if __name__=="__main__": main()
