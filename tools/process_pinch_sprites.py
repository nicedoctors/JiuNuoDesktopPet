"""Cut original-reference cheek-pinch sheets without generative re-editing."""

from __future__ import annotations

import json
from pathlib import Path

from PIL import Image, ImageDraw

from process_pair_sprites import digest, keyed, subject


ROOT = Path(__file__).resolve().parents[1]
SHEETS = ROOT / "assets" / "face_interactions" / "sheets"
PREVIEWS = ROOT / "assets" / "face_interactions" / "previews"
CHARACTERS = {
    "feibi": ("feibijiubi", ROOT / "assets" / "characters" / "feibijiubi" / "runtime", "pinch"),
    "feibi_right": ("feibijiubi", ROOT / "assets" / "characters" / "feibijiubi" / "runtime", "pinch_right"),
    "nuonuo": ("nuonuo", ROOT / "assets" / "sprites" / "runtime", "pinch"),
}


def build(name: str, character: str, runtime: Path, clip: str) -> dict:
    source = SHEETS / f"pinch_{name}.png"
    original = ROOT / "assets" / "characters" / character / "original.png"
    sheet = Image.open(source).convert("RGBA")
    if sheet.width != sheet.height or sheet.width < 1000:
        raise ValueError(f"{source}: expected square 4x4 sheet")

    columns = [round(index * sheet.width / 4) for index in range(5)]
    rows = [round(index * sheet.height / 4) for index in range(5)]
    if name == "feibi_right":
        rows = [0, 310, 623, 934, sheet.height]
    pieces = []
    for row in range(4):
        for col in range(4):
            cell = keyed(sheet.crop((columns[col], rows[row],
                                     columns[col + 1], rows[row + 1])))
            bounds = cell.getchannel("A").point(lambda value: 255 if value > 12 else 0).getbbox()
            if bounds is None or min(bounds[0], bounds[1],
                                     cell.width - bounds[2], cell.height - bounds[3]) < 3:
                raise ValueError(f"Source cell touches a cut edge: {name} {row},{col}")
            pieces.append((subject(cell), bounds[0], cell.width))
    max_size = 340 if character == "feibijiubi" else 290
    scale = min(max_size / max(piece.width for piece, _, _ in pieces),
                max_size / max(piece.height for piece, _, _ in pieces))
    destination = runtime / clip
    destination.mkdir(parents=True, exist_ok=True)
    PREVIEWS.mkdir(parents=True, exist_ok=True)
    preview = Image.new("RGBA", (1536, 1536), (237, 244, 249, 255))
    hashes = []
    for index, (piece, source_x, cell_width) in enumerate(pieces):
        piece = piece.resize((round(piece.width * scale), round(piece.height * scale)),
                             Image.Resampling.LANCZOS)
        x = round((384 - cell_width * scale) / 2 + source_x * scale)
        y = 374 - piece.height
        if x < 8 or y < 8:
            raise ValueError(f"Unsafe crop in {name} frame {index}")
        frame = Image.new("RGBA", (384, 384))
        frame.alpha_composite(piece, (x, y))
        output = destination / f"frame_{index:02d}.png"
        frame.save(output, optimize=True)
        hashes.append(digest(output))
        preview.alpha_composite(frame, ((index % 4) * 384, (index // 4) * 384))
    ImageDraw.Draw(preview).text((8, 8), f"pinch_{name}", fill=(23, 40, 64, 255))
    preview.save(PREVIEWS / f"pinch_{name}.png", optimize=True)
    return {
        "original": original.relative_to(ROOT).as_posix(),
        "originalSha256": digest(original),
        "source": source.relative_to(ROOT).as_posix(),
        "sourceSha256": digest(source),
        "runtime": destination.relative_to(ROOT).as_posix(),
        "runtimeSha256": hashes,
        "scale": round(scale, 6),
    }


def main() -> None:
    manifest = {name: build(name, *details) for name, details in CHARACTERS.items()}
    (PREVIEWS / "pinch-sprite-manifest.json").write_text(
        json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(PREVIEWS / "pinch-sprite-manifest.json")


if __name__ == "__main__":
    main()
