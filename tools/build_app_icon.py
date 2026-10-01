from __future__ import annotations

import argparse
from pathlib import Path

from PIL import Image


ICON_SIZES = [(16, 16), (20, 20), (24, 24), (32, 32), (40, 40), (48, 48), (64, 64), (128, 128), (256, 256)]


def main() -> None:
    parser = argparse.ArgumentParser(description="Build the multi-resolution Nuonuo Windows icon.")
    parser.add_argument("source", type=Path)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()

    with Image.open(args.source) as source:
        image = source.convert("RGBA")

    if image.width != image.height:
        side = max(image.size)
        square = Image.new("RGBA", (side, side), (0, 0, 0, 0))
        square.alpha_composite(image, ((side - image.width) // 2, (side - image.height) // 2))
        image = square

    args.output.parent.mkdir(parents=True, exist_ok=True)
    image.save(args.output, format="ICO", sizes=ICON_SIZES, bitmap_format="png")


if __name__ == "__main__":
    main()
