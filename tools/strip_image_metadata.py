#!/usr/bin/env python3
"""Strip non-rendering metadata from tracked PNG, GIF, and ICO files.

The cleaner never re-encodes pixel data. It removes PNG ancillary metadata
chunks, GIF comments and non-rendering application extensions, and applies the
same PNG cleanup to images embedded in ICO containers. Transparency and
animation-control data are retained because they affect rendering.
"""

from __future__ import annotations

import argparse
import collections
import hashlib
import json
import struct
import subprocess
import sys
from dataclasses import dataclass
from pathlib import Path


PNG_SIGNATURE = b"\x89PNG\r\n\x1a\n"
IMAGE_SUFFIXES = {".png", ".gif", ".ico"}
PNG_RENDERING_ANCILLARY = {
    b"tRNS", b"acTL", b"fcTL", b"fdAT",
    b"gAMA", b"cHRM", b"sRGB", b"sBIT", b"pHYs", b"iCCP",
}
GIF_LOOP_IDENTIFIERS = {b"NETSCAPE2.0", b"ANIMEXTS1.0"}


class ImageFormatError(ValueError):
    pass


@dataclass(frozen=True)
class CleanResult:
    data: bytes
    metadata: tuple[str, ...]
    rendering_digest: str


def tracked_images(root: Path) -> list[Path]:
    completed = subprocess.run(
        ["git", "-C", str(root), "ls-files", "-z"],
        check=True,
        stdout=subprocess.PIPE,
    )
    paths = completed.stdout.decode("utf-8").split("\0")
    return [root / path for path in paths if Path(path).suffix.lower() in IMAGE_SUFFIXES]


def png_chunks(data: bytes) -> list[tuple[bytes, bytes]]:
    if not data.startswith(PNG_SIGNATURE):
        raise ImageFormatError("invalid PNG signature")

    chunks: list[tuple[bytes, bytes]] = []
    offset = len(PNG_SIGNATURE)
    while offset < len(data):
        if offset + 12 > len(data):
            raise ImageFormatError("truncated PNG chunk header")
        length = struct.unpack_from(">I", data, offset)[0]
        end = offset + 12 + length
        if end > len(data):
            raise ImageFormatError("truncated PNG chunk payload")
        chunk_type = data[offset + 4 : offset + 8]
        chunks.append((chunk_type, data[offset:end]))
        offset = end
        if chunk_type == b"IEND":
            if offset != len(data):
                chunks.append((b"TAIL", data[offset:]))
            break

    if not chunks or not any(chunk_type == b"IEND" for chunk_type, _ in chunks):
        raise ImageFormatError("PNG has no IEND chunk")
    return chunks


def clean_png(data: bytes) -> CleanResult:
    kept = bytearray(PNG_SIGNATURE)
    metadata: list[str] = []
    rendering = hashlib.sha256()

    for chunk_type, raw_chunk in png_chunks(data):
        if chunk_type == b"TAIL":
            metadata.append("PNG:trailing-data")
            continue

        is_critical = 65 <= chunk_type[0] <= 90
        affects_rendering = is_critical or chunk_type in PNG_RENDERING_ANCILLARY
        if affects_rendering:
            kept.extend(raw_chunk)
            rendering.update(raw_chunk)
        else:
            metadata.append(f"PNG:{chunk_type.decode('latin-1')}")

    return CleanResult(bytes(kept), tuple(metadata), rendering.hexdigest())


def gif_sub_blocks_end(data: bytes, offset: int) -> int:
    while True:
        if offset >= len(data):
            raise ImageFormatError("truncated GIF sub-block")
        size = data[offset]
        offset += 1
        if size == 0:
            return offset
        offset += size
        if offset > len(data):
            raise ImageFormatError("truncated GIF sub-block payload")


def gif_header_end(data: bytes) -> int:
    if len(data) < 13 or data[:6] not in {b"GIF87a", b"GIF89a"}:
        raise ImageFormatError("invalid GIF header")
    packed = data[10]
    table_size = 3 * (2 ** ((packed & 0x07) + 1)) if packed & 0x80 else 0
    end = 13 + table_size
    if end > len(data):
        raise ImageFormatError("truncated GIF global color table")
    return end


def clean_gif(data: bytes) -> CleanResult:
    offset = gif_header_end(data)
    kept = bytearray(data[:offset])
    metadata: list[str] = []
    rendering = hashlib.sha256(data[:offset])

    while offset < len(data):
        marker = data[offset]
        start = offset

        if marker == 0x3B:
            kept.append(marker)
            rendering.update(bytes((marker,)))
            offset += 1
            if offset < len(data):
                metadata.append("GIF:trailing-data")
            break

        if marker == 0x2C:
            if offset + 10 > len(data):
                raise ImageFormatError("truncated GIF image descriptor")
            packed = data[offset + 9]
            local_table_size = 3 * (2 ** ((packed & 0x07) + 1)) if packed & 0x80 else 0
            sub_blocks = offset + 10 + local_table_size
            if sub_blocks >= len(data):
                raise ImageFormatError("truncated GIF image data")
            end = gif_sub_blocks_end(data, sub_blocks + 1)
            block = data[start:end]
            kept.extend(block)
            rendering.update(block)
            offset = end
            continue

        if marker != 0x21 or offset + 2 > len(data):
            raise ImageFormatError(f"unsupported GIF block marker 0x{marker:02x}")

        label = data[offset + 1]
        payload_start = offset + 2
        end = gif_sub_blocks_end(data, payload_start)
        block = data[start:end]

        if label in {0xF9, 0x01}:
            kept.extend(block)
            rendering.update(block)
        elif label == 0xFF:
            first_size = data[payload_start] if payload_start < len(data) else 0
            identifier_start = payload_start + 1
            identifier = data[identifier_start : identifier_start + first_size]
            if identifier in GIF_LOOP_IDENTIFIERS:
                kept.extend(block)
                rendering.update(block)
            else:
                metadata.append("GIF:application")
        elif label == 0xFE:
            metadata.append("GIF:comment")
        else:
            metadata.append(f"GIF:extension:0x{label:02x}")

        offset = end

    if not kept or kept[-1] != 0x3B:
        raise ImageFormatError("GIF has no trailer")
    return CleanResult(bytes(kept), tuple(metadata), rendering.hexdigest())


def clean_ico(data: bytes) -> CleanResult:
    if len(data) < 6:
        raise ImageFormatError("truncated ICO header")
    reserved, image_type, count = struct.unpack_from("<HHH", data, 0)
    if reserved != 0 or image_type != 1 or count == 0:
        raise ImageFormatError("invalid ICO header")
    entries_end = 6 + count * 16
    if entries_end > len(data):
        raise ImageFormatError("truncated ICO directory")

    entries: list[tuple[bytes, bytes, CleanResult | None]] = []
    metadata: list[str] = []
    rendering = hashlib.sha256()
    for index in range(count):
        entry_offset = 6 + index * 16
        entry = data[entry_offset : entry_offset + 16]
        size, offset = struct.unpack_from("<II", entry, 8)
        payload = data[offset : offset + size]
        if len(payload) != size:
            raise ImageFormatError(f"truncated ICO payload {index}")

        cleaned: CleanResult | None = None
        if payload.startswith(PNG_SIGNATURE):
            cleaned = clean_png(payload)
            metadata.extend(f"ICO[{index}]:{item}" for item in cleaned.metadata)
            rendering.update(bytes.fromhex(cleaned.rendering_digest))
        else:
            if len(payload) < 4:
                raise ImageFormatError(f"invalid ICO bitmap payload {index}")
            dib_header_size = struct.unpack_from("<I", payload, 0)[0]
            if dib_header_size > 40:
                raise ImageFormatError("extended ICO bitmap profiles need explicit privacy review")
            rendering.update(payload)
        entries.append((entry, payload, cleaned))

    output = bytearray(data[:6])
    output.extend(b"\0" * (count * 16))
    next_offset = entries_end
    for index, (entry, payload, cleaned) in enumerate(entries):
        cleaned_payload = cleaned.data if cleaned is not None else payload
        updated_entry = bytearray(entry)
        struct.pack_into("<II", updated_entry, 8, len(cleaned_payload), next_offset)
        entry_offset = 6 + index * 16
        output[entry_offset : entry_offset + 16] = updated_entry
        output.extend(cleaned_payload)
        next_offset += len(cleaned_payload)

    return CleanResult(bytes(output), tuple(metadata), rendering.hexdigest())


def clean_image(path: Path) -> CleanResult:
    data = path.read_bytes()
    suffix = path.suffix.lower()
    if suffix == ".png":
        return clean_png(data)
    if suffix == ".gif":
        return clean_gif(data)
    if suffix == ".ico":
        return clean_ico(data)
    raise ImageFormatError(f"unsupported image type: {suffix}")


def pixel_snapshot(paths: list[Path], root: Path) -> dict[str, str]:
    try:
        from PIL import Image, ImageSequence
    except ImportError as error:
        raise SystemExit("Pillow is required for --snapshot-pixels") from error

    snapshot: dict[str, str] = {}
    for path in paths:
        digest = hashlib.sha256()
        with Image.open(path) as image:
            frames = list(ImageSequence.Iterator(image))
            digest.update(str(image.size).encode("ascii"))
            digest.update(str(len(frames)).encode("ascii"))
            for frame in frames:
                rgba = frame.convert("RGBA")
                digest.update(rgba.tobytes())
                digest.update(str(frame.info.get("duration", 0)).encode("ascii"))
            digest.update(str(image.info.get("loop", 0)).encode("ascii"))
        snapshot[path.relative_to(root).as_posix()] = digest.hexdigest()
    return snapshot


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", type=Path, default=Path(__file__).resolve().parents[1])
    parser.add_argument("--strip", action="store_true", help="rewrite files after removing metadata")
    parser.add_argument("--snapshot-pixels", type=Path, help="write decoded pixel hashes as JSON")
    parser.add_argument("--compare-pixels", type=Path, help="compare decoded pixels with a prior snapshot")
    args = parser.parse_args()

    root = args.root.resolve()
    paths = tracked_images(root)

    if args.snapshot_pixels:
        snapshot = pixel_snapshot(paths, root)
        args.snapshot_pixels.write_text(
            json.dumps(snapshot, ensure_ascii=False, indent=2) + "\n",
            encoding="utf-8",
        )
        print(f"pixel snapshot: {len(snapshot)} files -> {args.snapshot_pixels}")
        return 0

    if args.compare_pixels:
        expected = json.loads(args.compare_pixels.read_text(encoding="utf-8"))
        actual = pixel_snapshot(paths, root)
        changed = sorted(path for path in set(expected) | set(actual) if expected.get(path) != actual.get(path))
        if changed:
            print("decoded pixel or animation changes detected:", file=sys.stderr)
            for path in changed:
                print(f"  {path}", file=sys.stderr)
            return 2
        print(f"decoded pixels unchanged: {len(actual)} files")
        return 0

    metadata_counts: collections.Counter[str] = collections.Counter()
    files_with_metadata = 0
    rewritten = 0
    rendering_before: dict[str, str] = {}

    for path in paths:
        result = clean_image(path)
        relative = path.relative_to(root).as_posix()
        rendering_before[relative] = result.rendering_digest
        if result.metadata:
            files_with_metadata += 1
            metadata_counts.update(result.metadata)
        if args.strip and result.data != path.read_bytes():
            path.write_bytes(result.data)
            rewritten += 1

    if args.strip:
        rendering_mismatches: list[str] = []
        remaining_metadata: list[str] = []
        for path in paths:
            result = clean_image(path)
            relative = path.relative_to(root).as_posix()
            if result.rendering_digest != rendering_before[relative]:
                rendering_mismatches.append(relative)
            if result.metadata:
                remaining_metadata.append(relative)
        if rendering_mismatches:
            raise SystemExit(f"rendering data changed in {len(rendering_mismatches)} files")
        if remaining_metadata:
            raise SystemExit(f"metadata remains in {len(remaining_metadata)} files")

    print(f"tracked images: {len(paths)}")
    print(f"files with removable metadata: {files_with_metadata}")
    if args.strip:
        print(f"rewritten files: {rewritten}")
        print("rendering data unchanged: yes")
        print("remaining removable metadata: 0")
    if metadata_counts:
        print("metadata blocks:")
        for name, count in sorted(metadata_counts.items()):
            print(f"  {name}: {count}")
    return 1 if files_with_metadata and not args.strip else 0


if __name__ == "__main__":
    raise SystemExit(main())
