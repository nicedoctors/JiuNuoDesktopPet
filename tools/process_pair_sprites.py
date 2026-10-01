"""Deterministically cut original-reference-generated 4x4 paired-action sheets.

This processor never feeds generated frames back to an image model. Selected
original-reference sheets are immutable inputs to deterministic cutting.
"""

from __future__ import annotations

import hashlib
import json
import sys
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFilter
from motion_alignment import face_bounds, foot_center

try:
    import cv2
except ImportError:
    cv2 = None


ROOT = Path(__file__).resolve().parents[1]
SPECS = {
    "cheek_feibi": ("feibijiubi", "pair_cheek"),
    "cheek_nuonuo": ("nuonuo", "pair_cheek"),
    "sleep_feibi": ("feibijiubi", "pair_sleep"),
    "sleep_nuonuo": ("nuonuo", "pair_sleep"),
    "feed_throw_feibi": ("feibijiubi", "pair_feed"),
    "feed_catch_nuonuo_upright": ("nuonuo", "pair_feed"),
    "icon_kick_feibi": ("feibijiubi", "pair_icon_kick"),
    "notice_feibi": ("feibijiubi", "pair_notice"),
    "notice_nuonuo": ("nuonuo", "pair_notice"),
    "nuzzle_feibi": ("feibijiubi", "pair_nuzzle"),
    "nuzzle_nuonuo": ("nuonuo", "pair_nuzzle"),
    "ball_feibi": ("feibijiubi", "pair_ball"),
    "ball_nuonuo": ("nuonuo", "pair_ball"),
}


def path_for(character: str, kind: str) -> Path:
    if kind == "runtime":
        return (ROOT / "assets" / "characters" / "feibijiubi" / "runtime"
                if character == "feibijiubi" else ROOT / "assets" / "sprites" / "runtime")
    return ROOT / "assets" / "pair_interactions" / kind


def digest(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def keyed(cell: Image.Image) -> Image.Image:
    rgba = np.array(cell.convert("RGBA"))
    if np.all(rgba[:, :, 3] == 255):
        if cv2 is None:
            raise RuntimeError("OpenCV is required to key opaque sprite sheets")
        rgb = rgba[:, :, :3]
        # The hat and coat are white too. Retain every white area enclosed by
        # the character outline instead of flood-keying its interior away.
        ink = (np.min(rgb, axis=2) < 238).astype(np.uint8)
        contours, _ = cv2.findContours(ink, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE)
        if not contours:
            raise ValueError("No silhouette in generated cell")
        outline = max(contours, key=cv2.contourArea)
        mask = np.zeros(ink.shape, np.uint8)
        cv2.drawContours(mask, [outline], -1, 255, thickness=cv2.FILLED)
        rgba[:, :, 3] = cv2.GaussianBlur(mask, (3, 3), .55)
    rgba[rgba[:, :, 3] < 8, :] = 0
    if cv2 is not None:
        count, labels, stats, _ = cv2.connectedComponentsWithStats(
            (rgba[:, :, 3] > 12).astype(np.uint8), 8)
    else:
        count = 0
    if count > 1:
        main = 1 + int(np.argmax(stats[1:, cv2.CC_STAT_AREA]))
        keep = cv2.dilate((labels == main).astype(np.uint8),
                          np.ones((3, 3), np.uint8), iterations=1) > 0
        rgba[~keep, :] = 0
    elif cv2 is None:
        alpha = rgba[:, :, 3] > 12
        ys, xs = np.nonzero(alpha)
        if xs.size == 0:
            raise ValueError("No silhouette in generated cell")
        nearest = np.argmin((xs - rgba.shape[1] / 2) ** 2 +
                            (ys - rgba.shape[0] / 2) ** 2)
        connected = Image.fromarray((alpha * 255).astype(np.uint8)).copy()
        ImageDraw.floodfill(connected, (int(xs[nearest]), int(ys[nearest])), 128)
        main = Image.fromarray((np.asarray(connected) == 128).astype(np.uint8) * 255)
        keep = np.asarray(main.filter(ImageFilter.MaxFilter(3))) > 0
        rgba[~keep, :] = 0
    if cv2 is not None:
        ink = ((rgba[:,:,3] > 220) & (rgba[:,:,:3].max(2) < 100)).astype(np.uint8)
        near_ink = cv2.dilate(ink,np.ones((5,5),np.uint8)) > 0
        fringe = (rgba[:,:,3] > 0) & (rgba[:,:,3] < 245) & (rgba[:,:,:3].min(2) > 180) & near_ink
        # Remove white background contamination only at the translucent exterior;
        # opaque white clothing and enclosed hat regions remain untouched.
        rgba[:,:,:3][fringe] = 35
    return Image.fromarray(rgba)


def cut_grid(sheet: Image.Image, name: str) -> list[Image.Image]:
    w, h = sheet.size
    if name == "icon_kick_feibi":
        # The selected original-reference sheet has unequal clear gutters.
        columns = [0, 360, 640, 965, w]
        rows = [0, 330, 640, 930, h]
    else:
        columns = [round(column * w / 4) for column in range(5)]
        rows = [round(row * h / 4) for row in range(5)]
    frames = []
    for row in range(4):
        for col in range(4):
            box = (columns[col], rows[row], columns[col + 1], rows[row + 1])
            frame = keyed(sheet.crop(box))
            frame.info['sheet_origin'] = box[:2]
            frames.append(frame)
    return frames


def cut(sheet: Image.Image, name: str) -> list[Image.Image]:
    rgba = np.asarray(sheet.convert('RGBA'))
    ink = ((rgba[:,:,3] > 12) & (rgba[:,:,:3].min(2) < 238)).astype(np.uint8)
    contours, _ = cv2.findContours(ink, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE)
    contours = sorted(contours, key=cv2.contourArea, reverse=True)
    if len(contours) < 16 or (len(contours) > 16 and cv2.contourArea(contours[16]) > .5*cv2.contourArea(contours[15])):
        raise ValueError(f'{name}: cannot identify sixteen separate character silhouettes')
    boxes = sorted([cv2.boundingRect(c) for c in contours[:16]],key=lambda b:b[1]+b[3]/2)
    ordered = [box for row in range(4) for box in sorted(boxes[row*4:row*4+4],key=lambda b:b[0])]
    frames = []
    for x,y,w,h in ordered:
        box = (max(0,x-4),max(0,y-4),min(sheet.width,x+w+4),min(sheet.height,y+h+4))
        frame = keyed(sheet.crop(box))
        frame.info['sheet_origin'] = box[:2]
        frames.append(frame)
    return frames


def subject(frame: Image.Image) -> Image.Image:
    bounds = frame.getchannel("A").point(lambda value: 255 if value > 12 else 0).getbbox()
    if bounds is None:
        raise ValueError("Empty generated cell")
    piece = frame.crop(bounds)
    origin = frame.info.get('sheet_origin',(0,0))
    piece.info['sheet_origin'] = (origin[0]+bounds[0],origin[1]+bounds[1])
    return piece


def build(name: str, character: str, clip: str) -> dict:
    source = path_for(character, "sheets") / f"{name}.png"
    original = (ROOT / "assets" / "characters" / character / "original.png")
    sheet = Image.open(source).convert("RGBA")
    if sheet.width != sheet.height or sheet.width < 1000:
        raise ValueError(f"{name}: source must be at least 1000px square")
    subjects = [subject(frame) for frame in cut(sheet, name)]
    annotated_subjects = [subject(frame) for frame in cut_grid(sheet, name)]
    max_width = max(piece.width for piece in subjects)
    max_height = max(piece.height for piece in subjects)
    reference_clip = "hat_open" if name == "feed_throw_feibi" else "idle"
    reference_index = 15 if name == "feed_throw_feibi" else 0
    reference = Image.open(path_for(character, "runtime") / reference_clip / f"frame_{reference_index:02d}.png")
    reference_face = face_bounds(reference)
    neutral_face = face_bounds(subjects[0])
    scale = (reference_face[2]-reference_face[0]) / (neutral_face[2]-neutral_face[0])
    feet = [foot_center(piece) for piece in subjects]
    # Calibrate against the same character's normal head size, not a sheet's
    # largest hand/hat silhouette. A single scale retains authored squash/stretch.
    scale = min(scale, 350/max_height,
                *(182/max(foot, 1) for foot in feet),
                *(182/max(piece.width-foot, 1) for piece, foot in zip(subjects, feet)))
    dest = path_for(character, "runtime") / clip
    dest.mkdir(parents=True, exist_ok=True)
    preview = Image.new("RGBA", (4 * 384, 4 * 384), (239, 244, 249, 255))
    hashes = []
    placements = []
    transforms = []
    previous_scale = min((275 if name == "feed_throw_feibi" else 350)/max(p.width for p in annotated_subjects),
                         (275 if name == "feed_throw_feibi" else 350)/max(p.height for p in annotated_subjects))
    for index, piece in enumerate(subjects):
        annotated = annotated_subjects[index]
        previous_x = round((384-round(annotated.width*previous_scale))/2)
        previous_y = 374-round(annotated.height*previous_scale)
        origin_x,origin_y = piece.info['sheet_origin']
        annotated_x,annotated_y = annotated.info['sheet_origin']
        piece = piece.resize((round(piece.width * scale), round(piece.height * scale)),
                             Image.Resampling.LANCZOS)
        canvas = Image.new("RGBA", (384, 384))
        x = round(192-feet[index]*scale)
        y = 374 - piece.height
        if x < 8 or y < 8:
            raise ValueError(f"{name} frame {index}: unsafe runtime crop")
        canvas.alpha_composite(piece, (x, y))
        placements.append([x,y])
        ratio = scale/previous_scale
        transforms.append({"Scale": ratio, "X": (x-previous_x*ratio+(annotated_x-origin_x)*scale)*4/3,
                           "Y": (y-previous_y*ratio+(annotated_y-origin_y)*scale)*4/3})
        output = dest / f"frame_{index:02d}.png"
        canvas.save(output, optimize=True)
        hashes.append(digest(output))
        preview.alpha_composite(canvas, ((index % 4) * 384, (index // 4) * 384))
    visual = path_for(character, "previews") / f"{name}.png"
    visual.parent.mkdir(parents=True, exist_ok=True)
    ImageDraw.Draw(preview).text((8, 8), name, fill=(23, 40, 64, 255))
    preview.save(visual, optimize=True)
    return {"character": character, "clip": clip,
            "original": original.relative_to(ROOT).as_posix(),
            "originalSha256": digest(original),
            "source": source.relative_to(ROOT).as_posix(),
            "sourceSha256": digest(source), "runtimeSha256": hashes,
            "scale": round(scale, 6), "placements": placements,
            "alignment": "normal-head-scale-and-ground-contact", "transforms": transforms,
            "preview": str(visual.relative_to(ROOT))}


def main() -> None:
    selected = sys.argv[1:] or list(SPECS)
    unknown = set(selected) - set(SPECS)
    if unknown:
        raise ValueError(f"Unknown sheet: {sorted(unknown)}")
    target = path_for("", "previews") / "pair-sprite-manifest.json"
    result = json.loads(target.read_text(encoding="utf-8")) if target.exists() else {}
    result.update({name: build(name, *SPECS[name]) for name in selected})
    target.write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    calibration = {f'{record["character"]}/{record["clip"]}': record["transforms"]
                   for record in result.values()}
    (ROOT/'assets/pair_interactions/calibration.json').write_text(
        json.dumps(calibration,indent=2)+'\n', encoding='utf-8')
    print(target)


if __name__ == "__main__":
    main()
