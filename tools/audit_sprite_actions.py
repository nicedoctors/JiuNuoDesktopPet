from __future__ import annotations

import argparse
import json
from pathlib import Path

import cv2
import numpy as np
from PIL import Image, ImageDraw, ImageFont


def checkerboard(size: tuple[int, int], cell: int = 16) -> Image.Image:
    canvas = Image.new("RGB", size, (245, 245, 245))
    draw = ImageDraw.Draw(canvas)
    for top in range(0, size[1], cell):
        for left in range(0, size[0], cell):
            if (left // cell + top // cell) % 2:
                draw.rectangle(
                    (left, top, min(size[0], left + cell), min(size[1], top + cell)),
                    fill=(220, 224, 228),
                )
    return canvas


def composite_preview(frame: Image.Image, size: int) -> Image.Image:
    resized = frame.resize((size, size), Image.Resampling.LANCZOS)
    preview = checkerboard((size, size), max(8, size // 16))
    preview.paste(resized.convert("RGB"), mask=resized.getchannel("A"))
    return preview


def audit_action(
    runtime_root: Path, preview_root: Path, action: str, *,
    playback_indices: tuple[int, ...] | None = None,
    playback_duration_ms: int = 100, playback_loop: bool = True,
    preview_name: str | None = None,
) -> dict[str, object]:
    frame_paths = [runtime_root / action / f"frame_{index:02d}.png" for index in range(16)]
    missing = [str(path) for path in frame_paths if not path.exists()]
    if missing:
        raise FileNotFoundError(f"{action} is missing frames: {missing}")

    frames = [Image.open(path).convert("RGBA") for path in frame_paths]
    contacts = Image.new("RGB", (1024, 1024), (245, 245, 245))
    gif_frames: list[Image.Image] = []
    frame_results: list[dict[str, object]] = []
    centroids: list[tuple[float, float]] = []

    for index, frame in enumerate(frames):
        rgba = np.array(frame)
        alpha = rgba[:, :, 3]
        mask = (alpha > 12).astype(np.uint8)
        points = cv2.findNonZero(mask)
        if points is None:
            raise ValueError(f"{action} frame {index:02d} is empty")
        x, y, width, height = cv2.boundingRect(points)
        count, _, stats, component_centroids = cv2.connectedComponentsWithStats(mask, 8)
        meaningful_components = [
            component
            for component in range(1, count)
            if stats[component, cv2.CC_STAT_AREA] >= 18
        ]
        largest = max(meaningful_components, key=lambda component: stats[component, cv2.CC_STAT_AREA])
        centroid = tuple(float(value) for value in component_centroids[largest])
        centroids.append(centroid)

        red = rgba[:, :, 0].astype(np.int16)
        green = rgba[:, :, 1].astype(np.int16)
        blue = rgba[:, :, 2].astype(np.int16)
        # Detect true chroma residue without treating the character's opaque
        # strawberry-pink tongue or costume highlights as background spill.
        strong_magenta = (
            (alpha > 12)
            & (alpha < 220)
            & (red > 180)
            & (blue > 170)
            & (green < 115)
            & ((np.minimum(red, blue) - green) > 82)
            & (np.abs(red - blue) < 65)
        )
        translucent_magenta = (
            (alpha > 12)
            & (alpha < 110)
            & (red > 165)
            & (blue > 165)
            & (green < 95)
            & ((np.minimum(red, blue) - green) > 65)
            & (np.abs(red - blue) < 70)
        )
        magenta_spill = int(np.count_nonzero(strong_magenta | translucent_magenta))
        corners_clear = all(
            alpha[row, column] == 0
            for row, column in ((0, 0), (0, -1), (-1, 0), (-1, -1))
        )
        frame_results.append({
            "frame": index,
            "size": [frame.width, frame.height],
            "bbox": [x, y, x + width, y + height],
            "meaningfulComponents": len(meaningful_components),
            "magentaSpillPixels": magenta_spill,
            "cornersClear": corners_clear,
        })

        tile = composite_preview(frame, 256)
        contacts.paste(tile, ((index % 4) * 256, (index // 4) * 256))
        gif_frames.append(composite_preview(frame, 384))

    actual_indices = playback_indices or tuple(range(16))
    continuity_indices = actual_indices + ((actual_indices[0],) if playback_indices is not None and playback_loop else ())
    adjacent_centroid_motion = [
        round(float(np.hypot(
            centroids[current][0] - centroids[previous][0],
            centroids[current][1] - centroids[previous][1],
        )), 3)
        for previous, current in zip(continuity_indices, continuity_indices[1:])
    ]
    bbox_widths = [result["bbox"][2] - result["bbox"][0] for result in frame_results]
    bbox_heights = [result["bbox"][3] - result["bbox"][1] for result in frame_results]
    median_width = float(np.median(bbox_widths))
    median_height = float(np.median(bbox_heights))
    minimum_silhouette_ratio = 0.40 if action in {"land", "roll", "satisfied"} else 0.55
    complete_silhouettes = all(
        width >= median_width * minimum_silhouette_ratio
        and height >= median_height * minimum_silhouette_ratio
        for width, height in zip(bbox_widths, bbox_heights, strict=True)
    )
    maximum_adjacent_motion = max(adjacent_centroid_motion, default=0)
    continuity_limit = 105 if action == "satisfied" else 65 if action in {"land", "roll"} else 36
    continuity_passed = maximum_adjacent_motion <= continuity_limit
    preview_root.mkdir(parents=True, exist_ok=True)
    name = preview_name or action
    contacts.save(preview_root / f"{name}_runtime_contact.png", optimize=True)
    playback_frames = [gif_frames[index] for index in actual_indices]
    playback_frames[0].save(
        preview_root / f"{name}.gif",
        save_all=True,
        append_images=playback_frames[1:],
        duration=playback_duration_ms,
        disposal=2,
        optimize=False,
        **({"loop": 0} if playback_loop else {}),
    )

    passed = complete_silhouettes and continuity_passed and all(
        result["size"] == [384, 384]
        and result["meaningfulComponents"] == 1
        # A handful of translucent subpixels can be produced by the final
        # Lanczos resize and are not visible chroma islands. Larger remnants
        # still fail deterministically.
        and result["magentaSpillPixels"] <= 12
        and result["cornersClear"]
        and result["bbox"][0] >= 10
        and result["bbox"][1] >= 10
        and result["bbox"][2] <= 374
        and result["bbox"][3] <= 374
        for result in frame_results
    )
    return {
        "action": action,
        "clip": name,
        "passedDeterministicChecks": passed,
        "completeSilhouettes": complete_silhouettes,
        "continuityPassed": continuity_passed,
        "continuityLimit": continuity_limit,
        "medianBoundingBox": [round(median_width, 2), round(median_height, 2)],
        "maximumAdjacentCentroidMotion": maximum_adjacent_motion,
        "playbackFrames": list(actual_indices),
        "playbackDurationMs": playback_duration_ms,
        "playbackLoop": playback_loop,
        "frames": frame_results,
    }


def main() -> None:
    parser = argparse.ArgumentParser(description="Audit and preview processed desktop-pet actions.")
    parser.add_argument("runtime_root", type=Path)
    parser.add_argument("preview_root", type=Path)
    parser.add_argument("actions", nargs="+")
    args = parser.parse_args()

    results = [audit_action(args.runtime_root, args.preview_root, action) for action in args.actions]
    labels = {
        "walk": "慢走",
        "fall": "坠落",
        "land": "Q弹着陆",
        "curious": "好奇探索",
        "sleep": "软糯睡眠",
        "hungry": "饥饿讨食",
        "climb": "攀爬窗沿",
        "roll": "饥饿打滚",
        "drag": "拖拽悬空",
        "jump": "窗口跳跃",
        "slide": "窗边下滑",
    }
    tile_size = 512
    label_height = 42
    columns = min(3, len(args.actions))
    rows = (len(args.actions) + columns - 1) // columns
    overview = Image.new("RGB", (columns * tile_size, rows * (tile_size + label_height)), (246, 247, 249))
    try:
        font = ImageFont.truetype("C:/Windows/Fonts/msyh.ttc", 25)
    except OSError:
        font = ImageFont.load_default()
    draw = ImageDraw.Draw(overview)
    for index, action in enumerate(args.actions):
        left = (index % columns) * tile_size
        top = (index // columns) * (tile_size + label_height)
        contact = Image.open(args.preview_root / f"{action}_runtime_contact.png").convert("RGB")
        contact = contact.resize((tile_size, tile_size), Image.Resampling.LANCZOS)
        draw.text((left + 16, top + 6), labels.get(action, action), fill=(43, 52, 59), font=font)
        overview.paste(contact, (left, top + label_height))
    overview.save(args.preview_root / "windows_life_overview.png", optimize=True)

    report = {
        "allPassed": all(result["passedDeterministicChecks"] for result in results),
        "actions": results,
    }
    report_path = args.preview_root / "audit.json"
    report_path.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
    print(json.dumps({
        "allPassed": report["allPassed"],
        "report": str(report_path),
        "maximumAdjacentCentroidMotion": {
            result["action"]: result["maximumAdjacentCentroidMotion"]
            for result in results
        },
    }, ensure_ascii=False, indent=2))
    if not report["allPassed"]:
        raise SystemExit(2)


if __name__ == "__main__":
    main()
