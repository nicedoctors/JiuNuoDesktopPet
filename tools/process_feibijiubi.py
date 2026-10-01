from __future__ import annotations

import argparse
import hashlib
import json
import math
from pathlib import Path

import cv2
import numpy as np
from PIL import Image, ImageDraw, ImageFont

from audit_sprite_actions import audit_action, composite_preview
from process_sprites import (
    alpha_bbox,
    remove_chroma_background,
    remove_saturated_chroma_spill,
    remove_tiny_fragments,
    save_icon,
)


CHARACTER_ROOT = Path(__file__).resolve().parents[1] / "assets" / "characters" / "feibijiubi"
ORIGINAL_SHA256 = "6b991b530a6c9ab038c762f56a22328d53b8615da1e59f7eace6e3e64139f815"
ACTIONS = (
    "idle", "run", "walk", "toss", "fall", "land", "curious", "sleep", "hungry",
    "climb", "drag", "jump", "slide", "roll", "lick", "chomp", "satisfied",
)
FRAME_ORDERS = {
    ("source-api-smooth-v2", "land"): (0, 1, 2, 3, 4, 5, 8, 7, 6, 10, 9, 11, 12, 13, 14, 15),
}
SELECTED_SOURCES = {
    **{action: "source-api" for action in ACTIONS},
    "curious": "source-api-smooth-v2", "land": "source-api-smooth-v2",
    "sleep": "source-api-continuity-v2", "jump": "source-api-jump-v2",
    "hungry": "source-api-hungry-v3",
}
PLAYBACK = {
    "idle": (tuple(range(16)), 120, True),
    "run": (tuple(range(16)), 58, True),
    "walk": (tuple(range(16)), 94, True),
    "toss": (tuple(range(8)), 55, False),
    "fall": (tuple(range(16)), 90, True),
    "land": (tuple(range(16)), 54, False),
    "curious": (tuple(range(8)) + tuple(range(6, 0, -1)), 112, True),
    "sleep": (tuple(range(8, 16)) + tuple(range(14, 8, -1)), 185, True),
    "hungry": (tuple(range(16)), 135, True),
    "climb": (tuple(range(16)), 71, True),
    "drag": (tuple(range(16)) + tuple(range(14, 0, -1)), 80, True),
    "jump": (tuple(range(16)), 63, False),
    "slide": (tuple(range(16)), 90, True),
    "roll": (tuple(range(16)), 74, False),
    "lick": (tuple(range(16)), 110, False),
    "chomp": (tuple(range(16)), 150, False),
    "satisfied": (tuple(range(16)), 95, False),
}
ADDITIONAL_CLIPS = {
    "sleep_enter": ("sleep", tuple(range(8)), 145),
    "sleep_exit": ("sleep", tuple(range(7, -1, -1)), 105),
    "satisfied_quick": ("satisfied", tuple(range(10)), 85),
}


def digest(file: Path) -> str:
    return hashlib.sha256(file.read_bytes()).hexdigest()


def write_json(file: Path, value: object) -> None:
    file.parent.mkdir(parents=True, exist_ok=True)
    file.write_text(json.dumps(value, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


def face_center_x(image: Image.Image) -> float | None:
    """Locate pale-pink facial skin, excluding yellow hair and the white hat."""
    rgba = np.asarray(image.convert("RGBA"))
    red, green, blue = [rgba[:, :, index].astype(np.int16) for index in range(3)]
    skin = (
        (rgba[:, :, 3] > 220)
        & (red > 210) & (green > 170) & (blue > 165)
        & ((red - green) > 3) & ((red - green) < 65)
        & (np.abs(green - blue) < 22)
    ).astype(np.uint8)
    # The brim is above the face and the uniform below it; neither can anchor a mouth.
    skin[:round(image.height * 0.30)] = 0
    skin[round(image.height * 0.73):] = 0
    count, _, stats, _ = cv2.connectedComponentsWithStats(skin, 8)
    candidates = [index for index in range(1, count) if stats[index, cv2.CC_STAT_AREA] >= 30]
    if not candidates:
        return None
    selected = max(candidates, key=lambda index: stats[index, cv2.CC_STAT_AREA])
    return float(stats[selected, cv2.CC_STAT_LEFT] + stats[selected, cv2.CC_STAT_WIDTH] / 2)


def decontaminate_dark_outline(image: Image.Image) -> Image.Image:
    """Remove dark magenta from antialiased outer ink without eroding its alpha."""
    rgba = np.array(image.convert("RGBA"))
    red, green, blue = [rgba[:, :, index].astype(np.int16) for index in range(3)]
    adjacent_background = cv2.dilate(
        (rgba[:, :, 3] == 0).astype(np.uint8), np.ones((5, 5), dtype=np.uint8),
    ) > 0
    contaminated = (
        (rgba[:, :, 3] > 0) & adjacent_background & (green < 75)
        & (red > green + 20) & (blue > green + 20) & (np.abs(red - blue) < 40)
    )
    rgba[:, :, 0][contaminated] = rgba[:, :, 1][contaminated]
    rgba[:, :, 2][contaminated] = rgba[:, :, 1][contaminated]
    return Image.fromarray(rgba)


def stabilize(frames: list[Image.Image], action: str) -> list[Image.Image]:
    subjects = [frame.crop(alpha_bbox(frame)) for frame in frames]
    scale = min(1.0, 350 / max(frame.width for frame in subjects), 350 / max(frame.height for frame in subjects))
    output = []
    for index, subject in enumerate(subjects):
        if scale < 1:
            subject = subject.resize(
                (round(subject.width * scale), round(subject.height * scale)), Image.Resampling.LANCZOS,
            )
        face_x = face_center_x(subject) if action in {"chomp", "lick"} else None
        if action in {"chomp", "lick"} and face_x is None:
            raise ValueError(f"{action}: facial anchor could not be located without including hair")
        left = round(156 - face_x) if face_x is not None else round(192 - subject.width / 2)
        if action == "chomp":
            mouth = mouth_geometry(subject)
            if mouth is not None and mouth["coloredArea"] > 200:
                left = round(135 - mouth["center"][0])
        if action == "slide":
            left = round(327 - slide_contact_x(subject))
        left = max(10, min(374 - subject.width, left))
        canvas = Image.new("RGBA", (384, 384), (0, 0, 0, 0))
        canvas.alpha_composite(subject, (left, 374 - subject.height))
        output.append(decontaminate_dark_outline(remove_saturated_chroma_spill(canvas)))
    return output


def slide_contact_x(subject: Image.Image) -> float:
    rgba = np.asarray(subject)
    ink = (rgba[:, :, 3] > 220) & (rgba[:, :, :3].max(axis=2) < 80)
    y0, y1 = round(subject.height * 0.42), round(subject.height * 0.68)
    points = np.where(ink[y0:y1])[1]
    if not len(points):
        raise ValueError("slide: window-contact glove outline could not be located")
    return float(points.max())


def normalize_roll_rotation(extracted: list[tuple[Image.Image, dict[str, object]]]) -> list[tuple[Image.Image, dict[str, object]]]:
    """Rotate complete source subjects rigidly; hat and eyes define body orientation."""
    targets = (4, 4, 19, 44, 74, 109, 144, 179, 214, 249, 284, 319, 354, 364, 364, 364)
    corrected = []
    for target, (subject, metadata) in zip(targets, extracted, strict=True):
        rgba = np.asarray(subject)
        rgb = rgba[:, :, :3].astype(np.int16)
        red, green, blue = [rgb[:, :, index] for index in range(3)]
        white = ((rgba[:, :, 3] > 220) & (rgb.min(axis=2) > 210) & (np.ptp(rgb, axis=2) < 30)).astype(np.uint8)
        count, _, stats, centers = cv2.connectedComponentsWithStats(white, 8)
        hat = max(range(1, count), key=lambda index: stats[index, cv2.CC_STAT_AREA])
        hat_center = centers[hat]
        purple = (
            (rgba[:, :, 3] > 220) & (red > 75) & (red < 205)
            & (green > 45) & (green < 165) & (blue > 110)
            & (blue > red + 12) & (red > green + 12)
        ).astype(np.uint8)
        count, _, stats, centers = cv2.connectedComponentsWithStats(purple, 8)
        eyes = sorted(range(1, count), key=lambda index: stats[index, cv2.CC_STAT_AREA], reverse=True)[:2]
        if len(eyes) < 2:
            raise ValueError("roll: both purple eye regions are required to determine orientation")
        eye_center = np.average(centers[eyes], axis=0, weights=stats[eyes, cv2.CC_STAT_AREA])
        delta = hat_center - eye_center
        source_angle = math.degrees(math.atan2(float(delta[0]), -float(delta[1])))
        rotation = (source_angle - target + 180) % 360 - 180
        subject = subject.rotate(rotation, resample=Image.Resampling.BICUBIC, expand=True)
        subject = subject.crop(alpha_bbox(subject))
        metadata.update({
            "sourceOrientationDegrees": round(source_angle, 3),
            "targetOrientationDegrees": target,
            "rigidRotationDegrees": round(rotation, 3),
        })
        corrected.append((subject, metadata))
    return corrected


def mouth_geometry(frame: Image.Image) -> dict[str, object] | None:
    rgba = np.asarray(frame.convert("RGBA"))
    red, green, blue = [rgba[:, :, index].astype(np.int16) for index in range(3)]
    mouth = (
        (rgba[:, :, 3] > 220) & (red > 135)
        & ((red - green) > 43) & ((red - blue) > 15)
        & (green < 175) & (blue < 195)
    ).astype(np.uint8)
    x0, y0, x1, y1 = alpha_bbox(frame)
    mouth[:round(y0 + (y1 - y0) * 0.30)] = 0
    mouth[round(y0 + (y1 - y0) * 0.80):] = 0
    count, _, stats, _ = cv2.connectedComponentsWithStats(mouth, 8)
    candidates = [index for index in range(1, count) if stats[index, cv2.CC_STAT_AREA] >= 5]
    if not candidates:
        return None
    selected = max(candidates, key=lambda index: stats[index, cv2.CC_STAT_AREA])
    x, y, width, height, area = [int(value) for value in stats[selected]]
    return {
        "bbox": [x, y, x + width, y + height],
        "center": [round(x + width / 2, 2), round(y + height / 2, 2)],
        "leftContact": [x, round(y + height / 2, 2)],
        "coloredArea": area,
    }


def extract_characters(sheet: Image.Image, action: str) -> list[tuple[Image.Image, dict[str, object]]]:
    """Recover whole original silhouettes even when a pose crosses a nominal grid line."""
    keyed = remove_saturated_chroma_spill(remove_chroma_background(sheet))
    rgba = np.array(keyed)
    count, labels, stats, centroids = cv2.connectedComponentsWithStats((rgba[:, :, 3] > 12).astype(np.uint8), 8)
    subjects = {}
    for label in range(1, count):
        x, y, width, height, area = [int(value) for value in stats[label]]
        if area < 1000:
            continue
        if x < 8 or y < 8 or x + width > sheet.width - 8 or y + height > sheet.height - 8:
            raise ValueError(f"{action}: original silhouette touches the sheet edge; regenerate from original")
        column = int(centroids[label, 0] // 512)
        row = int(centroids[label, 1] // 512)
        index = row * 4 + column
        if index in subjects or not 0 <= index < 16:
            raise ValueError(f"{action}: silhouettes cannot be uniquely assigned to the 4 x 4 grid")
        subject = rgba[y:y + height, x:x + width].copy()
        local_mask = (labels[y:y + height, x:x + width] == label).astype(np.uint8)
        local_mask = cv2.dilate(local_mask, np.ones((3, 3), dtype=np.uint8))
        subject[local_mask == 0] = 0
        subjects[index] = (Image.fromarray(subject), {
            "frame": index, "sourceBounds": [x, y, x + width, y + height],
            "completeSourceSilhouette": True,
            "crossesNominalCell": x < column * 512 or y < row * 512
                or x + width > (column + 1) * 512 or y + height > (row + 1) * 512,
        })
    if len(subjects) != 16:
        raise ValueError(f"{action}: expected 16 complete source characters, found {len(subjects)}")
    return [subjects[index] for index in range(16)]


def normalize_run_scale(extracted: list[tuple[Image.Image, dict[str, object]]]) -> list[tuple[Image.Image, dict[str, object]]]:
    """Correct the run sheet's small camera-scale drift using the white hat brim."""
    brim_widths = []
    for subject, _ in extracted:
        rgba = np.asarray(subject)
        rgb = rgba[:, :, :3].astype(np.int16)
        white = ((rgba[:, :, 3] > 220) & (rgb.min(axis=2) > 210) & (np.ptp(rgb, axis=2) < 30)).astype(np.uint8)
        white[round(subject.height * 0.43):] = 0
        count, _, stats, _ = cv2.connectedComponentsWithStats(white, 8)
        selected = max(range(1, count), key=lambda index: stats[index, cv2.CC_STAT_AREA])
        brim_widths.append(float(stats[selected, cv2.CC_STAT_WIDTH]))
    target_width = float(np.median(brim_widths))
    corrected = []
    for index, (subject, metadata) in enumerate(extracted):
        local_width = float(np.median([brim_widths[(index - 1) % 16], brim_widths[index], brim_widths[(index + 1) % 16]]))
        scale = float(np.clip(target_width / local_width, 0.93, 1.07))
        subject = subject.resize((round(subject.width * scale), round(subject.height * scale)), Image.Resampling.LANCZOS)
        metadata["uniformCameraScaleCorrection"] = round(scale, 5)
        corrected.append((subject, metadata))
    return corrected


def split_sheet(source_root: Path, action: str) -> dict[str, object]:
    source = source_root / f"{action}_sheet_api.png"
    receipt_path = source_root / f"{action}_generation.json"
    if not receipt_path.exists():
        raise ValueError(f"{action}: generation is still incomplete (no receipt)")
    receipt = json.loads(receipt_path.read_text(encoding="utf-8"))
    if receipt["sourceSha256"] != ORIGINAL_SHA256 or receipt["outputSha256"] != digest(source):
        raise ValueError(f"{action}: original/generated sheet lineage verification failed")
    sheet = Image.open(source).convert("RGBA")
    if sheet.size != (2048, 2048):
        raise ValueError(f"{action}: expected a 2048 x 2048 sheet")
    keyframes_dir = CHARACTER_ROOT / "keyframes" / action
    runtime_dir = CHARACTER_ROOT / "runtime" / action
    keyframes_dir.mkdir(parents=True, exist_ok=True)
    runtime_dir.mkdir(parents=True, exist_ok=True)
    extracted = extract_characters(sheet, action)
    order = FRAME_ORDERS.get((source_root.name, action), tuple(range(16)))
    extracted = [extracted[index] for index in order]
    for index, (_, metadata) in enumerate(extracted):
        metadata["sourceFrame"] = order[index]
        metadata["frame"] = index
    if action == "run":
        extracted = normalize_run_scale(extracted)
    if action == "roll":
        extracted = normalize_roll_rotation(extracted)
    source_scale = min(1.0, 480 / max(subject.width for subject, _ in extracted), 480 / max(subject.height for subject, _ in extracted))
    source_frames = []
    runtime_frames = []
    for index, (subject, metadata) in enumerate(extracted):
        if source_scale < 1:
            subject = subject.resize((round(subject.width * source_scale), round(subject.height * source_scale)), Image.Resampling.LANCZOS)
        keyed = Image.new("RGBA", (512, 512), (0, 0, 0, 0))
        keyed.alpha_composite(subject, (round(256 - subject.width / 2), 500 - subject.height))
        decontaminate_dark_outline(keyed).save(keyframes_dir / f"frame_{index:02d}.png", optimize=True)
        metadata["keyframeSha256"] = digest(keyframes_dir / f"frame_{index:02d}.png")
        reduced = keyed.resize((384, 384), Image.Resampling.LANCZOS)
        reduced = remove_saturated_chroma_spill(reduced)
        runtime_frames.append(remove_tiny_fragments(reduced, minimum_area=78))
        source_frames.append(metadata)
    stabilized = stabilize(runtime_frames, action)
    if action in {"walk", "run"}:
        from motion_alignment import align_locomotion
        stabilized = align_locomotion(stabilized)
    for index, frame in enumerate(stabilized):
        frame.save(runtime_dir / f"frame_{index:02d}.png", optimize=True)
        source_frames[index]["runtimeSha256"] = digest(runtime_dir / f"frame_{index:02d}.png")
        if action in {"chomp", "lick"}:
            source_frames[index]["mouthGeometry"] = mouth_geometry(frame)
        if action == "slide":
            bbox = alpha_bbox(frame)
            source_frames[index]["windowContactRuntimeX"] = bbox[0] + slide_contact_x(frame.crop(bbox))
    result = {
        "action": action, "sourceImage": source.relative_to(CHARACTER_ROOT).as_posix(),
        "sourceSha256": digest(source), "originalSha256": ORIGINAL_SHA256,
        "processor": "process_feibijiubi.py", "frameCount": 16, "frames": source_frames,
    }
    write_json(CHARACTER_ROOT / "previews" / f"{action}_processing.json", result)
    return result


def audit_available() -> dict[str, object]:
    preview_root = CHARACTER_ROOT / "previews"
    ready = [action for action in ACTIONS if (CHARACTER_ROOT / "runtime" / action / "frame_15.png").exists()]
    results = [audit_action(
        CHARACTER_ROOT / "runtime", preview_root, action,
        playback_indices=PLAYBACK[action][0], playback_duration_ms=PLAYBACK[action][1],
        playback_loop=PLAYBACK[action][2],
    ) for action in ready]
    clips = [audit_action(
        CHARACTER_ROOT / "runtime", preview_root / "clips", action,
        playback_indices=indices, playback_duration_ms=duration,
        playback_loop=False, preview_name=name,
    ) for name, (action, indices, duration) in ADDITIONAL_CLIPS.items() if action in ready]
    for action in ready:
        contact = Image.new("RGB", (512, 512), (246, 247, 249))
        for index in range(16):
            frame = Image.open(CHARACTER_ROOT / "runtime" / action / f"frame_{index:02d}.png").convert("RGBA")
            contact.paste(composite_preview(frame, 128), ((index % 4) * 128, (index // 4) * 128))
        contact.save(preview_root / f"{action}_128_contact.png", optimize=True)
        indices, duration, loop = PLAYBACK[action]
        previews = [composite_preview(Image.open(
            CHARACTER_ROOT / "runtime" / action / f"frame_{index:02d}.png",
        ).convert("RGBA"), 128) for index in indices]
        previews[0].save(
            preview_root / f"{action}_128.gif", save_all=True, append_images=previews[1:],
            duration=duration, disposal=2, optimize=False, **({"loop": 0} if loop else {}),
        )
    report = {
        "allPassed": all(result["passedDeterministicChecks"] for result in results + clips),
        "complete": len(ready) == len(ACTIONS), "expectedActionCount": len(ACTIONS),
        "actionCount": len(ready), "frameCount": len(ready) * 16, "actions": results,
        "additionalClips": clips,
    }
    write_json(preview_root / "audit.json", report)
    selections = {}
    for action in ready:
        processed = json.loads((preview_root / f"{action}_processing.json").read_text(encoding="utf-8"))
        selections[action] = {
            "sourceImage": processed["sourceImage"], "sourceSha256": processed["sourceSha256"],
            "processingRecord": f"previews/{action}_processing.json",
            "sourceFrameOrder": [frame["sourceFrame"] for frame in processed["frames"]],
        }
    write_json(CHARACTER_ROOT / "source-selections.json", {
        "originalImage": "original.png", "originalSha256": ORIGINAL_SHA256,
        "actionCount": len(ready), "nativeFramesPerAction": 16, "actions": selections,
    })
    if ready:
        tile, label_height, columns = 384, 34, 4
        rows = (len(ready) + columns - 1) // columns
        overview = Image.new("RGB", (columns * tile, rows * (tile + label_height)), (246, 247, 249))
        draw = ImageDraw.Draw(overview)
        try:
            font = ImageFont.truetype("C:/Windows/Fonts/arial.ttf", 23)
        except OSError:
            font = ImageFont.load_default()
        for index, action in enumerate(ready):
            x, y = (index % columns) * tile, (index // columns) * (tile + label_height)
            contact = Image.open(preview_root / f"{action}_runtime_contact.png").convert("RGB")
            overview.paste(contact.resize((tile, tile), Image.Resampling.LANCZOS), (x, y + label_height))
            draw.text((x + 12, y + 4), action, fill=(43, 52, 59), font=font)
        overview.save(preview_root / "overview.png", optimize=True)
    return report


def main() -> None:
    parser = argparse.ArgumentParser(description="Deterministically cut and audit original-source Feibi Jiubi animation sheets.")
    parser.add_argument("--source-root", help="Override the final per-action source selections with a directory inside this character's asset folder.")
    parser.add_argument("--actions", nargs="+", choices=ACTIONS)
    parser.add_argument("--audit-only", action="store_true")
    args = parser.parse_args()
    if digest(CHARACTER_ROOT / "original.png") != ORIGINAL_SHA256:
        raise ValueError("Original image SHA-256 mismatch")
    failures = []
    if not args.audit_only:
        ready = args.actions or ACTIONS
        for action in ready:
            try:
                source_root = (CHARACTER_ROOT / (args.source_root or SELECTED_SOURCES[action])).resolve()
                if not source_root.is_relative_to(CHARACTER_ROOT.resolve()):
                    raise ValueError("Source directory must remain inside this character's asset folder")
                split_sheet(source_root, action)
                print(f"Processed: {action}")
            except ValueError as error:
                failures.append(str(error))
        if (CHARACTER_ROOT / "keyframes" / "idle" / "frame_00.png").exists():
            save_icon(CHARACTER_ROOT / "keyframes", CHARACTER_ROOT / "pet.ico")
    report = audit_available()
    print(json.dumps({
        "processedActions": report["actionCount"], "runtimeFrames": report["frameCount"],
        "allPassed": report["allPassed"], "complete": report["complete"], "processingFailures": failures,
        "auditFailures": [item["action"] for item in report["actions"] if not item["passedDeterministicChecks"]],
    }, ensure_ascii=False))
    if failures or not report["allPassed"]:
        raise SystemExit(2)


if __name__ == "__main__":
    main()
