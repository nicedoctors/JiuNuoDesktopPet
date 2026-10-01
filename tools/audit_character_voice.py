"""Audit local PCM WAV recordings without playback, transcription, or conversion."""

from __future__ import annotations

import argparse
import hashlib
import json
import math
import shutil
import statistics
import struct
from datetime import datetime, timezone
from pathlib import Path


SILENCE_AMPLITUDE = 10 ** (-60 / 20)
CLIPPING_AMPLITUDE = 0.999


def sha256(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def decibels(amplitude: float) -> float | None:
    return round(20 * math.log10(amplitude), 3) if amplitude > 0 else None


def audit_wav(path: Path) -> dict:
    data = path.read_bytes()
    result = {"file": path.name, "bytes": len(data), "sourceSha256": sha256(data)}
    if len(data) < 12 or data[:4] != b"RIFF" or data[8:12] != b"WAVE":
        raise ValueError(f"{path.name}: expected a RIFF/WAVE file")
    riff_end = struct.unpack_from("<I", data, 4)[0] + 8
    if riff_end != len(data):
        raise ValueError(f"{path.name}: RIFF size does not match file length")

    chunks = []
    cursor = 12
    while cursor < riff_end:
        if cursor + 8 > riff_end:
            raise ValueError(f"{path.name}: truncated chunk header")
        chunk_id, size = struct.unpack_from("<4sI", data, cursor)
        start, end = cursor + 8, cursor + 8 + size
        if end > riff_end:
            raise ValueError(f"{path.name}: truncated chunk data")
        chunks.append((chunk_id, data[start:end]))
        cursor = end + (size % 2)
    if cursor != riff_end:
        raise ValueError(f"{path.name}: missing RIFF padding")
    formats = [body for name, body in chunks if name == b"fmt "]
    payloads = [body for name, body in chunks if name == b"data"]
    if len(formats) != 1 or len(payloads) != 1 or len(formats[0]) < 16:
        raise ValueError(f"{path.name}: expected exactly one fmt and data chunk")

    fmt, pcm = formats[0], payloads[0]
    tag, channels, rate, byte_rate, align, bits = struct.unpack_from("<HHIIHH", fmt)
    encoding = tag
    valid_bits = bits
    channel_mask = None
    if tag == 0xFFFE:
        if len(fmt) < 40 or struct.unpack_from("<H", fmt, 16)[0] < 22:
            raise ValueError(f"{path.name}: invalid WAVE_FORMAT_EXTENSIBLE header")
        valid_bits, channel_mask = struct.unpack_from("<HI", fmt, 18)
        encoding = struct.unpack_from("<I", fmt, 24)[0]
        if fmt[28:40] != bytes.fromhex("00001000800000aa00389b71"):
            raise ValueError(f"{path.name}: unsupported extensible subformat")
    if encoding != 1 or bits not in (8, 16, 24, 32):
        raise ValueError(f"{path.name}: only integer PCM is supported by this audit")
    if not 0 < valid_bits <= bits or channels < 1 or rate < 1:
        raise ValueError(f"{path.name}: invalid PCM parameters")
    sample_width = bits // 8
    if align != channels * sample_width or byte_rate != rate * align:
        raise ValueError(f"{path.name}: inconsistent PCM block alignment / byte rate")
    if not pcm or len(pcm) % align:
        raise ValueError(f"{path.name}: empty or incomplete PCM frames")

    frames = len(pcm) // align
    maximum = float(1 << (bits - 1))
    energy = 0.0
    peak = 0.0
    clipped = 0
    nonzero = 0
    first_active = None
    last_active = None
    for frame in range(frames):
        frame_peak = 0.0
        for channel in range(channels):
            offset = frame * align + channel * sample_width
            raw = pcm[offset : offset + sample_width]
            value = raw[0] - 128 if bits == 8 else int.from_bytes(raw, "little", signed=True)
            amplitude = abs(value / maximum)
            energy += amplitude * amplitude
            peak = max(peak, amplitude)
            frame_peak = max(frame_peak, amplitude)
            clipped += amplitude >= CLIPPING_AMPLITUDE
            nonzero += value != 0
        if frame_peak > SILENCE_AMPLITUDE:
            if first_active is None:
                first_active = frame
            last_active = frame
    sample_count = frames * channels
    rms = math.sqrt(energy / sample_count)
    clipping_ratio = clipped / sample_count
    warnings = []
    if nonzero == 0:
        warnings.append("all_silent_pcm")
    elif first_active is None:
        warnings.append("no_audio_above_minus_60_dbfs")
    if clipping_ratio >= 0.001:
        warnings.append("potential_heavy_clipping_at_least_0.1_percent")
    result.update(
        {
            "riffValid": True,
            "format": "WAVE_FORMAT_EXTENSIBLE_PCM" if tag == 0xFFFE else "PCM",
            "channels": channels,
            "sampleRate": rate,
            "bitsPerSample": bits,
            "validBitsPerSample": valid_bits,
            "channelMask": channel_mask,
            "frames": frames,
            "durationSeconds": round(frames / rate, 6),
            "pcmBytes": len(pcm),
            "pcmSha256": sha256(pcm),
            "nonzeroSamples": nonzero,
            "peakDbfs": decibels(peak),
            "rmsDbfs": decibels(rms),
            "nearFullScaleSampleCount": clipped,
            "nearFullScaleSampleRatio": round(clipping_ratio, 8),
            "leadingSilenceSeconds": round((first_active if first_active is not None else frames) / rate, 6),
            "trailingSilenceSeconds": round((frames - last_active - 1 if last_active is not None else frames) / rate, 6),
            "warnings": warnings,
        }
    )
    return result


def duplicate_groups(records: list[dict], key: str) -> list[list[str]]:
    groups: dict[str, list[str]] = {}
    for record in records:
        groups.setdefault(record[key], []).append(record["file"])
    return [names for names in groups.values() if len(names) > 1]


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", type=Path, required=True)
    parser.add_argument("--destination", type=Path, required=True)
    parser.add_argument("--report", type=Path, required=True)
    parser.add_argument("--copy", action="store_true", help="Copy verified originals; never replace a different destination file.")
    args = parser.parse_args()
    source = args.source.resolve(strict=True)
    destination = args.destination.resolve()
    if source == destination:
        raise ValueError("The original source and project audio directories must differ")
    paths = sorted(source.glob("*.wav"), key=lambda path: path.name)
    if not paths:
        raise ValueError("No WAV recordings were found")
    records = [audit_wav(path) for path in paths]
    levels = [record["rmsDbfs"] for record in records if record["rmsDbfs"] is not None]
    median_rms = statistics.median(levels) if levels else None
    for record in records:
        if median_rms is not None and record["rmsDbfs"] is not None:
            delta = round(record["rmsDbfs"] - median_rms, 3)
            record["rmsDifferenceFromMedianDb"] = delta
            if abs(delta) >= 9:
                record["warnings"].append("rms_outlier_at_least_9_db_from_collection_median")
        target = destination / record["file"]
        if target.exists() and sha256(target.read_bytes()) != record["sourceSha256"]:
            raise ValueError(f"{record['file']}: destination differs; original project file was not overwritten")
    if args.copy:
        destination.mkdir(parents=True, exist_ok=True)
        for source_path, record in zip(paths, records, strict=True):
            target = destination / source_path.name
            if not target.exists():
                shutil.copy2(source_path, target)
            record["projectSha256"] = sha256(target.read_bytes())
            record["sourceUnchanged"] = sha256(source_path.read_bytes()) == record["sourceSha256"]
            if record["projectSha256"] != record["sourceSha256"] or not record["sourceUnchanged"]:
                raise ValueError(f"{source_path.name}: copy or original-integrity verification failed")
    report = {
        "schemaVersion": 1,
        "auditedAtUtc": datetime.now(timezone.utc).isoformat(),
        "method": "Local binary PCM inspection only; no playback, transcription, upload, resampling, or gain changes.",
        "silenceThresholdDbfs": -60,
        "nearFullScaleThreshold": CLIPPING_AMPLITUDE,
        "rmsOutlierThresholdDb": 9,
        "recordingCount": len(records),
        "totalBytes": sum(record["bytes"] for record in records),
        "totalDurationSeconds": round(sum(record["durationSeconds"] for record in records), 6),
        "medianRmsDbfs": median_rms,
        "duplicateFileGroups": duplicate_groups(records, "sourceSha256"),
        "duplicatePcmGroups": duplicate_groups(records, "pcmSha256"),
        "files": records,
    }
    args.report.parent.mkdir(parents=True, exist_ok=True)
    args.report.write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({key: value for key, value in report.items() if key != "files"}, ensure_ascii=False, indent=2))
    for record in records:
        print(f"{record['file']}: {record['durationSeconds']:.3f}s, peak={record['peakDbfs']} dBFS, RMS={record['rmsDbfs']} dBFS, warnings={record['warnings']}")


if __name__ == "__main__":
    main()
