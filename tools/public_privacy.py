#!/usr/bin/env python3
"""Audit publishable files; remove descriptive media metadata without re-encoding."""

from __future__ import annotations

import argparse
import hashlib
import json
import re
import shutil
import struct
import subprocess
from collections import Counter
from pathlib import Path

from strip_image_metadata import clean_image, pixel_snapshot
from notice_manifest import verified_notices

TEXT_SUFFIXES = {".cs", ".csproj", ".vcxproj", ".sln", ".xaml", ".manifest", ".rc",
                 ".c", ".h", ".md", ".txt", ".json", ".py", ".mjs", ".js", ".ps1", ".yml", ".yaml", ".props"}
MEDIA_SUFFIXES = {".png", ".gif", ".ico"}
PATTERNS = {
    "credential": re.compile(r"(?:ghp_[A-Za-z0-9]{30,}|github_pat_[A-Za-z0-9_]{40,}|sk-[A-Za-z0-9_-]{32,}|AKIA[A-Z0-9]{16}|-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----)"),
    "personal-path": re.compile(r"(?i)\b[A-Z]:(?:\\+|/)(?:Users|Game|junk|A[.]library)(?:\\+|/)[^\r\n\"'<>]*"),
    "email": re.compile(r"[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}"),
}
PUBLIC_EMAIL = "nicedoctors@users.noreply.github.com"
WAV_METADATA = {b"bext", b"iXML", b"axml", b"ID3 ", b"id3 ", b"DISP"}


def public_files(root: Path) -> list[Path]:
    result = subprocess.run(["git", "-C", str(root), "ls-files", "--cached", "--others",
                             "--exclude-standard", "-z"], check=True, stdout=subprocess.PIPE)
    return sorted({root / name for name in result.stdout.decode("utf-8").split("\0")
                   if name and (root / name).is_file()})


def wav_clean(data: bytes) -> tuple[bytes, list[str], str]:
    if data[:4] != b"RIFF" or data[8:12] != b"WAVE":
        raise ValueError("Unsupported WAV container; review without transcoding")
    declared_end = struct.unpack_from("<I", data, 4)[0] + 8
    if declared_end > len(data):
        raise ValueError("Truncated WAV")
    kept, metadata, render = bytearray(b"WAVE"), [], hashlib.sha256()
    position = 12
    while position < declared_end:
        if position + 8 > declared_end:
            raise ValueError("Truncated WAV chunk")
        kind, size = data[position:position + 4], struct.unpack_from("<I", data, position + 4)[0]
        end = position + 8 + size + size % 2
        if end > declared_end:
            raise ValueError("Invalid WAV chunk size")
        raw = data[position:end]
        descriptive = kind in WAV_METADATA or kind == b"LIST" and raw[8:12] == b"INFO"
        if descriptive:
            metadata.append("WAV:" + kind.decode("ascii", errors="replace"))
        else:
            kept.extend(raw)
            render.update(raw)
        position = end
    if declared_end < len(data):
        metadata.append("WAV:trailing-data")
    return b"RIFF" + struct.pack("<I", len(kept)) + kept, metadata, render.hexdigest()


def text_findings(text: str, *, upstream_notice: bool = False) -> list[str]:
    findings = []
    for label, pattern in PATTERNS.items():
        hits = pattern.findall(text)
        if label == "email":
            if upstream_notice:
                continue
            hits = [hit for hit in hits if hit != PUBLIC_EMAIL]
        if hits:
            findings.append(label)
    return findings


def audit(root: Path, paths: list[Path]) -> dict:
    notices, findings = verified_notices(root)
    chunks = Counter()
    counts = Counter()
    for path in paths:
        suffix = path.suffix.lower()
        relative = path.relative_to(root).as_posix()
        if suffix in MEDIA_SUFFIXES:
            result = clean_image(path)
            counts["images"] += 1
            chunks.update(result.metadata)
            if result.metadata:
                findings.append({"file": relative, "categories": ["image-metadata"]})
        elif suffix == ".wav":
            _, metadata, _ = wav_clean(path.read_bytes())
            counts["recordings"] += 1
            chunks.update(metadata)
            if metadata:
                findings.append({"file": relative, "categories": ["audio-metadata"]})
        elif suffix in TEXT_SUFFIXES or path.name in {".gitignore", ".gitattributes", "LICENSE", "NOTICE"}:
            try:
                text = path.read_text(encoding="utf-8-sig")
            except UnicodeError:
                findings.append({"file": relative, "categories": ["encoding-review"]})
                continue
            counts["textFiles"] += 1
            issues = text_findings(text, upstream_notice=relative in notices)
            if issues:
                findings.append({"file": relative, "categories": issues})
    return {"checkedFiles": len(paths), "counts": dict(counts),
            "metadataBlocks": dict(chunks), "findings": findings}


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, default=Path(__file__).resolve().parents[1])
    parser.add_argument("--apply-media", action="store_true")
    parser.add_argument("--backup-root", type=Path)
    parser.add_argument("--report", type=Path)
    options = parser.parse_args()
    root = options.root.resolve()
    paths = public_files(root)
    changes, hashes = [], {}
    if options.apply_media:
        if not options.backup_root:
            parser.error("--apply-media requires a private --backup-root outside the project")
        backup = options.backup_root.resolve()
        if backup == root or root in backup.parents or not backup.is_dir():
            parser.error("Backup must be an existing private directory outside the project")
        for path in paths:
            old = path.read_bytes() if path.suffix.lower() in MEDIA_SUFFIXES | {".wav"} else None
            if old is None:
                continue
            if path.suffix.lower() == ".wav":
                new, _, digest = wav_clean(old)
                if wav_clean(new)[2] != digest:
                    raise ValueError("WAV audio data changed")
            else:
                new = clean_image(path).data
            if old == new:
                continue
            saved = backup / "media-before-cleanup" / path.relative_to(root)
            saved.parent.mkdir(parents=True, exist_ok=True)
            if saved.exists():
                raise ValueError("Refusing to overwrite an existing private backup")
            shutil.copy2(path, saved)
            before = pixel_snapshot([path], root) if path.suffix.lower() in MEDIA_SUFFIXES else None
            path.write_bytes(new)
            if before is not None and before != pixel_snapshot([path], root):
                path.write_bytes(old)
                raise ValueError("Decoded pixels changed; original restored")
            hashes[hashlib.sha256(old).hexdigest()] = hashlib.sha256(new).hexdigest()
            changes.append(path.relative_to(root).as_posix())
        if hashes:
            pattern = re.compile("|".join(hashes), re.IGNORECASE)
            for path in paths:
                if path.suffix.lower() not in TEXT_SUFFIXES:
                    continue
                data = path.read_bytes()
                try:
                    text = data.decode("utf-8")
                except UnicodeError:
                    continue
                updated = pattern.sub(lambda match: hashes[match[0].lower()].upper()
                                      if match[0].isupper() else hashes[match[0].lower()], text)
                if updated != text:
                    saved = backup / "hash-records-before-cleanup" / path.relative_to(root)
                    saved.parent.mkdir(parents=True, exist_ok=True)
                    if saved.exists():
                        raise ValueError("Refusing to overwrite a hash-record backup")
                    shutil.copy2(path, saved)
                    path.write_bytes(updated.encode("utf-8"))
            (backup / "media-hash-map.json").write_text(json.dumps(hashes, indent=2), encoding="utf-8")
    report = audit(root, paths)
    report["changedMedia"] = changes
    report["unchangedRendering"] = bool(options.apply_media)
    if options.report:
        options.report.write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(report, ensure_ascii=False, indent=2))
    return 1 if report["findings"] else 0


if __name__ == "__main__":
    raise SystemExit(main())
