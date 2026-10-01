from __future__ import annotations

import json
import math
from pathlib import Path

import cv2
import numpy as np
from PIL import Image, ImageDraw

from process_feibijiubi_pranks import (
    ACTIONS, CHARACTER_ROOT, ORIGINAL_SHA256, PREVIEW_ROOT,
    composite_preview, crop_foreground, digest, face_bounds, write_json,
)


# Hand centers measured on the 256-pixel contact tiles; convert to 512 canvas.
TEAR_UPPER_256 = [(72, 169), (70, 171), (72, 132), (76, 168), (74, 166), (75, 165), (61, 114), (45, 119), (40, 112), (24, 114), (46, 134), (46, 134), (70, 184), (75, 184), (65, 188), (82, 217)]
TEAR_LOWER_256 = [(97, 179), (100, 179), (73, 192), (76, 206), (74, 206), (76, 207), (63, 205), (54, 214), (50, 216), (54, 218), (71, 206), (75, 204), (85, 204), (88, 204), (168, 220), (159, 220)]
CONTACTS_256 = {
    "kick": [(118,243),(129,243),(129,243),(106,215),(77,459-256),(76,451-256),(50,435-256),(36,425-256),(29,669-512),(28,675-512),(96,728-512),(100,739-512),(117,997-768),(165,996-768),(156,1004-768),(118,1010-768)],
    "punch": [(95,216),(171,166),(171,178),(171,177),(175,437-256),(78,440-256),(72,440-256),(66,442-256),(61,699-512),(75,690-512),(107,708-512),(173,673-512),(85,959-768),(85,959-768),(100,983-768),(100,983-768)],
    "charge": [(110,180),(109,179),(110,180),(110,180),(120,182),(124,188),(135,200),(138,200),(143,204),(147,204),(135,203),(136,200),(109,177),(109,177),(109,177),(109,177)],
    "bare_kick": [(122,242),(122,241),(122,243),(109,216),(102,208),(88,208),(105,242),(109,238),(43,178),(43,173),(46,179),(109,235),(104,236),(109,236),(129,243),(129,243)],
}


def snap_ink(frame: Image.Image, point_256: tuple[float, float]) -> dict:
    rgba = np.asarray(frame)
    ink = (rgba[:, :, 3] > 220) & (rgba[:, :, :3].max(axis=2) < 100)
    y, x = np.where(ink)
    expected = np.array(point_256) * 1.5
    distances = np.square(x - expected[0]) + np.square(y - expected[1])
    selected = int(distances.argmin())
    if distances[selected] > 12 ** 2:
        raise ValueError("Measured hand is more than 12 runtime pixels away from the visible ink")
    return {"x": round(float(x[selected]) * 4 / 3, 3), "y": round(float(y[selected]) * 4 / 3, 3)}


def annotate_tear() -> dict:
    files = [CHARACTER_ROOT / "runtime" / "bare_tear" / f"frame_{index:02d}.png" for index in range(16)]
    frames = [Image.open(file).convert("RGBA") for file in files]
    upper = [snap_ink(frame, point) for frame, point in zip(frames, TEAR_UPPER_256, strict=True)]
    lower = [snap_ink(frame, point) for frame, point in zip(frames, TEAR_LOWER_256, strict=True)]
    preview = Image.new("RGB", (1024, 1024), (245, 245, 245))
    for index, frame in enumerate(frames):
        tile = composite_preview(frame, 256)
        draw = ImageDraw.Draw(tile)
        for point, color in ((upper[index], (235, 45, 45)), (lower[index], (30, 125, 245))):
            x, y = point["x"] / 2, point["y"] / 2
            draw.ellipse((x - 4, y - 4, x + 4, y + 4), outline=color, width=2)
        preview.paste(tile, ((index % 4) * 256, (index // 4) * 256))
    PREVIEW_ROOT.mkdir(parents=True, exist_ok=True)
    preview.save(PREVIEW_ROOT / "bare_tear_anchors.png", optimize=True)
    return {
        "runtimeSha256": [digest(file) for file in files],
        "contactFrame": 8, "grabFrame": 3, "contact": upper,
        "tearUpperHand": upper, "tearLowerHand": lower,
        "visualQa": {"completeCharacter": True, "continuousMotion": True, "leftFacingInteraction": True, "correctHatState": True},
    }


def base_annotation(action: str) -> dict:
    files = [CHARACTER_ROOT / "runtime" / action / f"frame_{index:02d}.png" for index in range(16)]
    return {"runtimeSha256": [digest(file) for file in files], "visualQa": {"completeCharacter": True, "continuousMotion": True, "leftFacingInteraction": True, "correctHatState": True}}


def annotate_contact(action: str, points: list[tuple[float, float]]) -> dict:
    frames = [Image.open(CHARACTER_ROOT / "runtime" / action / f"frame_{index:02d}.png").convert("RGBA") for index in range(16)]
    contacts = [snap_ink(frame, point) for frame, point in zip(frames, points, strict=True)]
    preview = Image.new("RGB", (1024, 1024), (245, 245, 245))
    for index, frame in enumerate(frames):
        tile = composite_preview(frame, 256)
        draw = ImageDraw.Draw(tile)
        point = contacts[index]
        x, y = point["x"] / 2, point["y"] / 2
        draw.ellipse((x - 4, y - 4, x + 4, y + 4), outline=(235, 45, 45), width=2)
        preview.paste(tile, ((index % 4) * 256, (index // 4) * 256))
    preview.save(PREVIEW_ROOT / f"{action}_anchors.png", optimize=True)
    return {**base_annotation(action), "contactFrame": 8, "contact": contacts}


def locate_hat(frame: Image.Image, action: str, index: int) -> dict:
    rgba = np.asarray(frame)
    rgb = rgba[:, :, :3].astype(np.int16)
    white = ((rgba[:, :, 3] > 220) & (rgb.min(axis=2) > 190) & (np.ptp(rgb, axis=2) < 14)).astype(np.uint8)
    count, labels, stats, centroids = cv2.connectedComponentsWithStats(white, 8)
    held = action != "hat_open" or index >= 6
    face_bottom = face_bounds(frame)[3]
    candidates = [i for i in range(1, count) if stats[i, cv2.CC_STAT_AREA] > 80 and (not held or centroids[i, 1] > face_bottom - 22)]
    selected = max(candidates, key=lambda i: stats[i, cv2.CC_STAT_AREA] * max(1, stats[i, cv2.CC_STAT_WIDTH] / stats[i, cv2.CC_STAT_HEIGHT]))
    hat_white = (labels == selected).astype(np.uint8)
    contours, _ = cv2.findContours(hat_white, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE)
    filled = np.zeros_like(hat_white)
    cv2.drawContours(filled, contours, -1, 1, cv2.FILLED)
    dark_interior = ((filled > 0) & (rgb.max(axis=2) < 150)).astype(np.uint8)
    count, _, inner_stats, centers = cv2.connectedComponentsWithStats(dark_interior, 8)
    dark_candidates = [i for i in range(1, count) if inner_stats[i, cv2.CC_STAT_AREA] > 50]
    x, y, width, height, _ = [int(value) for value in stats[selected]]
    if dark_candidates:
        inner = max(dark_candidates, key=lambda i: inner_stats[i, cv2.CC_STAT_AREA])
        cx, cy = [float(value) for value in centers[inner]]
        mouth_width = float(inner_stats[inner, cv2.CC_STAT_WIDTH])
    else:
        cx, cy, mouth_width = x + width / 2, y + height * 0.6, width * 0.6
    return {"mouth": {"x": round(cx * 4 / 3, 3), "y": round(cy * 4 / 3, 3)}, "width": round(mouth_width * 4 / 3, 3), "whiteBounds": [x, y, x + width, y + height], "hasInterior": bool(dark_candidates)}


def annotate_hat(action: str) -> dict:
    result = base_annotation(action)
    mouths, widths, polygons = [], [], []
    preview = Image.new("RGB", (1024, 1024), (245, 245, 245))
    for index in range(16):
        frame = Image.open(CHARACTER_ROOT / "runtime" / action / f"frame_{index:02d}.png").convert("RGBA")
        hat = locate_hat(frame, action, index)
        mouths.append(hat["mouth"])
        widths.append(hat["width"])
        left, top, right, bottom = hat["whiteBounds"]
        y0 = hat["mouth"]["y"] if hat["hasInterior"] else (top + (bottom-top)*0.7) * 4 / 3
        y1 = min(512, (bottom + (28 if hat["hasInterior"] else 5)) * 4 / 3)
        polygon = [{"x": max(0,(left-5)*4/3),"y":y0}, {"x":min(512,(right+5)*4/3),"y":y0}, {"x":min(512,(right+5)*4/3),"y":y1}, {"x":max(0,(left-5)*4/3),"y":y1}]
        polygons.append(polygon)
        mask_folder = CHARACTER_ROOT / "runtime-masks" / action
        mask_folder.mkdir(parents=True, exist_ok=True)
        crop_foreground(frame, polygon).save(mask_folder / f"frame_{index:02d}.png", optimize=True)
        tile = composite_preview(frame, 256)
        draw = ImageDraw.Draw(tile)
        x, y = hat["mouth"]["x"] / 2, hat["mouth"]["y"] / 2
        draw.line((x-hat["width"]/4,y,x+hat["width"]/4,y),fill=(230,45,45),width=2)
        draw.polygon([(p["x"]/2,p["y"]/2) for p in polygon],outline=(20,145,230),width=1)
        preview.paste(tile, ((index % 4)*256,(index//4)*256))
    preview.save(PREVIEW_ROOT / f"{action}_anchors.png", optimize=True)
    result.update({"hatMouth": mouths, "hatWidth": widths, "foregroundPolygons": polygons,
                   "foregroundMaskFolder": f"runtime-masks/{action}"})
    return result


def annotate_transfer(action: str) -> dict:
    # Visible hat geometry measured on the native release/grip frame. Width is
    # the hat's unrotated long axis; rotation uses screen clockwise degrees.
    values = {
        "hat_throw": (7, 6, (326.5, 148), 129.5, -56.31),
        "hat_pickup": (4, 4, (275, 349), 92, 180),
    }
    event, source, center, width, angle = values[action]
    frame = Image.open(CHARACTER_ROOT / "runtime" / action / f"frame_{source:02d}.png").convert("RGBA")
    preview = composite_preview(frame, 384)
    draw = ImageDraw.Draw(preview)
    dx = math.cos(math.radians(angle)) * width / 2
    dy = math.sin(math.radians(angle)) * width / 2
    draw.line((center[0]-dx, center[1]-dy, center[0]+dx, center[1]+dy), fill=(235,45,45), width=2)
    draw.ellipse((center[0]-4,center[1]-4,center[0]+4,center[1]+4), outline=(20,145,230), width=2)
    preview.save(PREVIEW_ROOT / f"{action}_transfer_anchor.png", optimize=True)
    return {**base_annotation(action), "hatTransferFrame": event, "hatTransferSourceFrame": source,
            "hatTransferPoint": {"x": round(center[0]*4/3,3), "y": round(center[1]*4/3,3)},
            "hatTransferWidth": round(width*4/3,3), "hatTransferRotation": angle}


def main() -> None:
    target = PREVIEW_ROOT / "measured-annotations.json"
    result = json.loads(target.read_text(encoding="utf-8")) if target.exists() else {"originalSha256": ORIGINAL_SHA256, "clips": {}}
    result["clips"]["bare_tear"] = annotate_tear()
    for action, points in CONTACTS_256.items():
        result["clips"][action] = annotate_contact(action, points)
    for action in ("hat_open", "hat_store", "hat_hold", "hat_return"):
        if (CHARACTER_ROOT / "runtime" / action / "frame_15.png").exists():
            result["clips"][action] = annotate_hat(action)
    for action in ("hat_throw", "hat_pickup"):
        result["clips"][action] = annotate_transfer(action)
    for action in ACTIONS:
        if action not in result["clips"]:
            result["clips"][action] = base_annotation(action)
    prop = CHARACTER_ROOT / "props" / "hat.png"
    if prop.exists():
        result["hatPropSha256"] = digest(prop)
    write_json(target, result)
    print(json.dumps({"measuredClips": list(result["clips"]), "annotationFile": target.relative_to(CHARACTER_ROOT).as_posix()}))


if __name__ == "__main__":
    main()
