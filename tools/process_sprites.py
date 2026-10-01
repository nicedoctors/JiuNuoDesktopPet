from __future__ import annotations

import argparse
from pathlib import Path
import sys

import cv2
import numpy as np
from PIL import Image


CLIP_NAMES = ("idle", "run", "chomp", "satisfied")


def remove_chroma_background(image: Image.Image) -> Image.Image:
    """Remove only magenta regions connected to a cell edge.

    Edge-connected keying preserves the character's pink eyes and red costume even
    when their colors are locally close to the chroma background.
    """
    rgba = np.array(image.convert("RGBA"))
    rgb = rgba[:, :, :3].astype(np.int16)
    red, green, blue = rgb[:, :, 0], rgb[:, :, 1], rgb[:, :, 2]
    # Keep this close to the actual #FF00FF key. The older broad threshold also
    # admitted strawberry/salmon pinks; if a tongue highlight touched its outer
    # antialiasing, the edge-connected flood could punch a transparent hole into
    # the tongue. Real chroma pixels have red and blue both strongly dominant
    # over green, while the character's pink eyes and tongue do not.
    chroma_candidate = (
        (red > 150)
        & (blue > 145)
        & (green < 145)
        & ((np.minimum(red, blue) - green) > 52)
        & (np.abs(red - blue) < 95)
    ).astype(np.uint8)

    count, labels = cv2.connectedComponents(chroma_candidate, 8)
    edge_labels = np.unique(
        np.concatenate((labels[0, :], labels[-1, :], labels[:, 0], labels[:, -1]))
    )
    edge_labels = edge_labels[edge_labels != 0]
    if count > 1 and edge_labels.size:
        background = np.isin(labels, edge_labels)
        rgba[background, 3] = 0
        rgba[background, :3] = 0

    return Image.fromarray(rgba)


def remove_saturated_chroma_spill(image: Image.Image) -> Image.Image:
    """Remove isolated, highly saturated magenta key pixels left on soft hair edges.

    The character's pink eye gradient is deliberately lower saturation, so this
    narrow cleanup does not desaturate or punch holes through her eyes.
    """
    rgba = np.array(image.convert("RGBA"))
    red = rgba[:, :, 0].astype(np.int16)
    green = rgba[:, :, 1].astype(np.int16)
    blue = rgba[:, :, 2].astype(np.int16)
    alpha = rgba[:, :, 3]
    strong_spill = (
        (alpha > 0)
        & (red > 180)
        & (blue > 170)
        & (green < 115)
        & ((np.minimum(red, blue) - green) > 82)
        & (np.abs(red - blue) < 65)
    )
    # Resampling can leave a handful of translucent magenta fringe pixels. They
    # are safe to remove only at low alpha; opaque salmon pixels belong to the
    # tongue/costume and must remain foreground.
    translucent_fringe = (
        (alpha > 0)
        & (alpha < 110)
        & (red > 165)
        & (blue > 165)
        & (green < 95)
        & ((np.minimum(red, blue) - green) > 65)
        & (np.abs(red - blue) < 70)
    )
    spill = strong_spill | translucent_fringe
    rgba[spill, 3] = 0
    rgba[spill, :3] = 0
    return Image.fromarray(rgba)


def remove_tiny_fragments(image: Image.Image, minimum_area: int = 140) -> Image.Image:
    rgba = np.array(image.convert("RGBA"))
    alpha = rgba[:, :, 3]
    alpha[alpha < 12] = 0
    count, labels, stats, _ = cv2.connectedComponentsWithStats((alpha > 12).astype(np.uint8), 8)
    largest_label = 1 + int(np.argmax(stats[1:, cv2.CC_STAT_AREA])) if count > 1 else 0

    for label in range(1, count):
        area = stats[label, cv2.CC_STAT_AREA]
        if label != largest_label or area < minimum_area:
            rgba[labels == label, 3] = 0

    rgba[rgba[:, :, 3] == 0, :3] = 0
    return Image.fromarray(rgba)


def alpha_bbox(image: Image.Image) -> tuple[int, int, int, int]:
    alpha = np.array(image.getchannel("A"))
    points = cv2.findNonZero((alpha > 12).astype(np.uint8))
    if points is None:
        return (0, 0, image.width, image.height)
    x, y, width, height = cv2.boundingRect(points)
    return (x, y, x + width, y + height)


def face_center_x(image: Image.Image) -> float | None:
    """Find the largest pale-skin region and use it as a stable head anchor.

    Hair follow-through changes the overall silhouette width dramatically during
    suction. Centering that silhouette makes the face jump in the opposite
    direction, so chomp frames are aligned to the face instead.
    """
    rgba = np.array(image.convert("RGBA"))
    red = rgba[:, :, 0].astype(np.int16)
    green = rgba[:, :, 1].astype(np.int16)
    blue = rgba[:, :, 2].astype(np.int16)
    alpha = rgba[:, :, 3]
    skin = (
        (alpha > 32)
        & (red > 205)
        & (green > 155)
        & (blue > 145)
        & ((red - green) > 4)
        & ((red - green) < 85)
        & ((green - blue) > -5)
        & ((green - blue) < 65)
    ).astype(np.uint8)
    count, _, stats, centroids = cv2.connectedComponentsWithStats(skin, 8)
    candidates = [
        label
        for label in range(1, count)
        if stats[label, cv2.CC_STAT_AREA] >= 30
    ]
    if not candidates:
        return None
    largest = max(candidates, key=lambda label: stats[label, cv2.CC_STAT_AREA])
    return float(centroids[largest, 0])


def stabilize_frames(
    frames: list[Image.Image], *, anchor_face_horizontally: bool = False
) -> list[Image.Image]:
    boxes = [alpha_bbox(frame) for frame in frames]
    subjects = [frame.crop(box) for frame, box in zip(frames, boxes, strict=True)]
    maximum_width = max(subject.width for subject in subjects)
    maximum_height = max(subject.height for subject in subjects)
    global_scale = min(1.0, 350 / maximum_width, 350 / maximum_height)
    target_center_x = 192
    target_bottom = 374
    output: list[Image.Image] = []

    for subject in subjects:
        if global_scale < 1.0:
            new_size = (
                max(1, int(round(subject.width * global_scale))),
                max(1, int(round(subject.height * global_scale))),
            )
            subject = subject.resize(new_size, Image.Resampling.LANCZOS)

        canvas = Image.new("RGBA", (384, 384), (0, 0, 0, 0))
        if anchor_face_horizontally:
            local_face_x = face_center_x(subject)
            if local_face_x is None:
                paste_x = int(round(target_center_x - subject.width / 2))
            else:
                paste_x = int(round(156 - local_face_x))
                paste_x = max(10, min(374 - subject.width, paste_x))
        else:
            paste_x = int(round(target_center_x - subject.width / 2))
        paste_y = target_bottom - subject.height
        canvas.alpha_composite(subject, (paste_x, paste_y))
        output.append(canvas)

    return output


def split_sheet(sheet_path: Path, clip_name: str, keyframes_root: Path, runtime_root: Path) -> None:
    sheet = Image.open(sheet_path).convert("RGBA")
    keyframes_dir = keyframes_root / clip_name
    runtime_dir = runtime_root / clip_name
    keyframes_dir.mkdir(parents=True, exist_ok=True)
    runtime_dir.mkdir(parents=True, exist_ok=True)

    runtime_frames: list[Image.Image] = []
    for row in range(4):
        top = round(row * sheet.height / 4)
        bottom = round((row + 1) * sheet.height / 4)
        for column in range(4):
            left = round(column * sheet.width / 4)
            right = round((column + 1) * sheet.width / 4)
            index = row * 4 + column

            keyframe = sheet.crop((left, top, right, bottom)).resize((512, 512), Image.Resampling.LANCZOS)
            keyframe = remove_chroma_background(keyframe)
            keyframe = remove_saturated_chroma_spill(keyframe)
            keyframe = remove_tiny_fragments(keyframe)
            keyframe.save(keyframes_dir / f"frame_{index:02d}.png", optimize=True)

            runtime_frame = keyframe.resize((384, 384), Image.Resampling.LANCZOS)
            runtime_frame = remove_saturated_chroma_spill(runtime_frame)
            runtime_frames.append(remove_tiny_fragments(runtime_frame, minimum_area=78))

    stabilized = stabilize_frames(
        runtime_frames,
        anchor_face_horizontally=clip_name.casefold() in {"chomp", "lick"},
    )
    if clip_name.casefold() in {"walk", "run"}:
        from motion_alignment import align_locomotion
        stabilized = align_locomotion(stabilized)
    for index, runtime_frame in enumerate(stabilized):
        runtime_frame.save(runtime_dir / f"frame_{index:02d}.png", optimize=True)


def save_icon(keyframes_root: Path, icon_path: Path) -> None:
    first_frame = Image.open(keyframes_root / "idle" / "frame_00.png").convert("RGBA")
    first_frame.thumbnail((256, 256), Image.Resampling.LANCZOS)
    first_frame.save(
        icon_path,
        format="ICO",
        sizes=[(32, 32), (48, 48), (64, 64), (128, 128), (256, 256)],
    )


def main() -> None:
    if len(sys.argv) > 1 and sys.argv[1] == "actions":
        parser = argparse.ArgumentParser(
            description="Cleanly split one or more additional native 4x4 animation sheets."
        )
        parser.add_argument("mode", choices=("actions",))
        parser.add_argument("keyframes_root", type=Path)
        parser.add_argument("runtime_root", type=Path)
        parser.add_argument(
            "action_sheets",
            nargs="+",
            metavar="ACTION=SHEET",
            help="Action name and its original generated sheet path.",
        )
        args = parser.parse_args()
        for action_sheet in args.action_sheets:
            if "=" not in action_sheet:
                parser.error(f"Expected ACTION=SHEET, received: {action_sheet}")
            action, sheet_text = action_sheet.split("=", 1)
            if not action or not sheet_text:
                parser.error(f"Expected ACTION=SHEET, received: {action_sheet}")
            split_sheet(
                Path(sheet_text),
                action,
                args.keyframes_root,
                args.runtime_root,
            )
        return

    parser = argparse.ArgumentParser(description="Cleanly split four native 4x4 animation sheets.")
    parser.add_argument("idle_sheet", type=Path)
    parser.add_argument("run_sheet", type=Path)
    parser.add_argument("chomp_sheet", type=Path)
    parser.add_argument("satisfied_sheet", type=Path)
    parser.add_argument("keyframes_root", type=Path)
    parser.add_argument("runtime_root", type=Path)
    parser.add_argument("icon_path", type=Path)
    args = parser.parse_args()

    sheets = (args.idle_sheet, args.run_sheet, args.chomp_sheet, args.satisfied_sheet)
    for clip_name, sheet_path in zip(CLIP_NAMES, sheets, strict=True):
        split_sheet(sheet_path, clip_name, args.keyframes_root, args.runtime_root)
    save_icon(args.keyframes_root, args.icon_path)


if __name__ == "__main__":
    main()
