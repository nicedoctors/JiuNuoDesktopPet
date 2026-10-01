from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path

import cv2
import numpy as np
from PIL import Image, ImageDraw, ImageFont, ImageOps

from audit_sprite_actions import audit_action, composite_preview
from process_feibijiubi import (
    CHARACTER_ROOT, ORIGINAL_SHA256, decontaminate_dark_outline, digest,
    extract_characters, write_json,
)
from process_sprites import alpha_bbox, remove_chroma_background, remove_saturated_chroma_spill


ACTIONS = (
    "prank_sneak", "kick", "punch", "charge", "recoil", "hat_open", "hat_store",
    "hat_hold", "hat_return", "gloat", "hat_throw", "bare_idle", "bare_run",
    "bare_kick", "bare_tear", "bare_recover", "bare_drag", "bare_fall",
    "hat_pickup", "hat_wear",
)
CONTACT_ACTIONS = {"kick", "punch", "charge", "bare_kick", "bare_tear"}
HAT_ACTIONS = {"hat_open", "hat_store", "hat_hold", "hat_return"}
LOOPS = {"prank_sneak", "hat_hold", "bare_idle", "bare_run", "bare_drag", "bare_fall"}
FRAME_SECONDS = {
    **{action: 0.09 for action in ACTIONS},
    "prank_sneak": 0.105, "hat_open": 0.11, "hat_store": 0.11,
    "hat_hold": 0.14, "hat_return": 0.11, "bare_idle": 0.12,
    "bare_run": 0.058, "bare_kick": 0.11, "bare_tear": 0.17,
    "bare_drag": 0.08, "hat_pickup": 0.11, "hat_wear": 0.11,
}
PREVIEW_ROOT = CHARACTER_ROOT / "previews" / "pranks"
SOURCE_ORDERS = {}


def inside_character(file: Path) -> Path:
    resolved = file.resolve()
    if not resolved.is_relative_to(CHARACTER_ROOT.resolve()):
        raise ValueError("Asset path must remain inside the Feibi character directory")
    return resolved


def face_bounds(frame: Image.Image) -> tuple[int, int, int, int]:
    """Use the pale facial skin rather than changing glove/hat silhouette width."""
    rgba = np.asarray(frame.convert("RGBA"))
    red, green, blue = [rgba[:, :, index].astype(np.int16) for index in range(3)]
    skin = (
        (rgba[:, :, 3] > 220) & (red > 210) & (green > 170) & (blue > 165)
        & ((red - green) > 3) & ((red - green) < 65) & (np.abs(green - blue) < 22)
    ).astype(np.uint8)
    skin[:round(frame.height * 0.15)] = 0
    skin[round(frame.height * 0.79):] = 0
    count, _, stats, _ = cv2.connectedComponentsWithStats(skin, 8)
    candidates = [i for i in range(1, count) if stats[i, cv2.CC_STAT_AREA] >= 40]
    if not candidates:
        raise ValueError("The complete facial anchor is not recognizable; inspect or regenerate from original")
    selected = max(candidates, key=lambda i: stats[i, cv2.CC_STAT_AREA])
    x, y, width, height, _ = [int(value) for value in stats[selected]]
    return x, y, x + width, y + height


def normalize_subjects(extracted: list[tuple[Image.Image, dict]], action: str) -> list[tuple[Image.Image, dict]]:
    hair_widths = []
    hair_centers = []
    for subject, _ in extracted:
        rgba = np.asarray(subject)
        red, green, blue = [rgba[:, :, i].astype(np.int16) for i in range(3)]
        gold = ((rgba[:, :, 3] > 220) & (red > 180) & (green > 130)
                & (red > green + 4) & (green > blue + 15)).astype(np.uint8)
        count, _, stats, _ = cv2.connectedComponentsWithStats(gold, 8)
        selected = max(range(1, count), key=lambda i: stats[i, cv2.CC_STAT_AREA])
        hair_widths.append(float(stats[selected, cv2.CC_STAT_WIDTH]))
        hair_centers.append(float(stats[selected, cv2.CC_STAT_LEFT] + stats[selected, cv2.CC_STAT_WIDTH] / 2))
    median_width = float(np.median(hair_widths))
    corrected = []
    for index, (subject, record) in enumerate(extracted):
        neighbors = hair_widths[max(0, index - 1):min(16, index + 2)]
        camera_scale = float(np.clip(median_width / float(np.median(neighbors)), 0.88, 1.12))
        subject = subject.resize((round(subject.width * camera_scale), round(subject.height * camera_scale)), Image.Resampling.LANCZOS)
        corrected.append((subject, {**record, "uniformCameraScaleCorrection": round(camera_scale, 7), "hairCenterSourceX": hair_centers[index] * camera_scale}))
    extracted = corrected
    reference = Image.open(CHARACTER_ROOT / "runtime" / "idle" / "frame_00.png").convert("RGBA")
    reference_face = face_bounds(reference)
    faces = [face_bounds(subject) for subject, _ in extracted]
    if action == "hat_wear":
        # The brim covers the face in native cel5; use the intact hair silhouette
        # and adjacent visible face-to-hair offsets, not a tiny exposed skin sliver.
        offsets = [(faces[i][0] + faces[i][2]) / 2 - extracted[i][1]["hairCenterSourceX"] for i in (4, 6)]
        center = extracted[5][1]["hairCenterSourceX"] + float(np.median(offsets))
        width = float(np.median([faces[i][2] - faces[i][0] for i in (4, 6)]))
        bounds = faces[5]
        faces[5] = (center - width / 2, bounds[1], center + width / 2, bounds[3])
        extracted[5][1]["occludedFaceAnchor"] = "hair silhouette plus adjacent visible face-to-hair offset"
    target_width = reference_face[2] - reference_face[0]
    scale = target_width / float(np.median([bounds[2] - bounds[0] for bounds in faces]))
    neutral_frame = 15 if action in {"recoil", "gloat", "bare_recover", "hat_wear"} else 0
    hatless_start = action.startswith("bare_") or action in {"hat_store", "hat_hold", "hat_return", "hat_pickup"}
    reference_height = alpha_bbox(reference)[3] - alpha_bbox(reference)[1]
    target_height = reference_height * (0.91 if hatless_start else 1.0)
    scale = min(scale, target_height / extracted[neutral_frame][0].height)
    # One scale for the complete clip preserves limb anticipation and squash/stretch.
    # Fixed facial X avoids recentering the body when a kicking foot extends left.
    for (subject, _), bounds in zip(extracted, faces, strict=True):
        center = (bounds[0] + bounds[2]) / 2
        scale = min(scale, 182 / max(center, 1), 182 / max(subject.width - center, 1), 350 / subject.height)
    output = []
    for (subject, source_record), bounds in zip(extracted, faces, strict=True):
        scaled = subject.resize((round(subject.width * scale), round(subject.height * scale)), Image.Resampling.LANCZOS)
        face_x = (bounds[0] + bounds[2]) / 2 * scale
        left, top = round(192 - face_x), 374 - scaled.height
        if left < 9 or top < 9 or left + scaled.width > 375:
            raise ValueError("Whole silhouette cannot fit without cropping")
        canvas = Image.new("RGBA", (384, 384), (0, 0, 0, 0))
        canvas.alpha_composite(scaled, (left, top))
        canvas = decontaminate_dark_outline(remove_saturated_chroma_spill(canvas))
        record = dict(source_record)
        record.update({"uniformClipScale": round(scale, 7), "runtimePlacement": [left, top], "faceAnchorCanvasX": 256.0})
        output.append((canvas, record))
    return output


def checked_receipt(source_root: Path, action: str) -> tuple[Path, dict]:
    source = inside_character(source_root / f"{action}_sheet_api.png")
    receipt_path = source_root / f"{action}_generation.json"
    if not receipt_path.exists():
        raise ValueError(f"{action}: no completed generation receipt; no placeholder accepted")
    receipt = json.loads(receipt_path.read_text(encoding="utf-8"))
    prompt = inside_character(CHARACTER_ROOT / receipt["prompt"])
    if receipt["sourceSha256"] != ORIGINAL_SHA256 or receipt["outputSha256"] != digest(source):
        raise ValueError(f"{action}: original/source lineage mismatch")
    if receipt["promptSha256"] != digest(prompt) or receipt["model"] != "gpt-image-2":
        raise ValueError(f"{action}: prompt/model provenance mismatch")
    return source, receipt


def process_action(source_root: Path, action: str) -> dict:
    source, receipt = checked_receipt(source_root, action)
    sheet = Image.open(source).convert("RGBA")
    if sheet.size != (2048, 2048):
        raise ValueError(f"{action}: expected 2048-square original sheet")
    extracted = extract_characters(sheet, action)
    if action == "bare_drag":
        extracted = [(ImageOps.mirror(subject), {**record, "nativeHorizontalMirrorCorrected": True}) if 8 <= index <= 11 else (subject, record) for index, (subject, record) in enumerate(extracted)]
    order = SOURCE_ORDERS.get((source_root.name, action), tuple(range(16)))
    extracted = [extracted[index] for index in order]
    normalized = normalize_subjects(extracted, action)
    target = CHARACTER_ROOT / "runtime" / action
    target.mkdir(parents=True, exist_ok=True)
    keyframes = CHARACTER_ROOT / "keyframes" / action
    keyframes.mkdir(parents=True, exist_ok=True)
    frames = []
    for index, ((runtime, record), (subject, _)) in enumerate(zip(normalized, extracted, strict=True)):
        runtime_file = target / f"frame_{index:02d}.png"
        runtime.save(runtime_file, optimize=True)
        source_scale = min(1, 480 / subject.width, 480 / subject.height)
        subject = subject.resize((round(subject.width * source_scale), round(subject.height * source_scale)), Image.Resampling.LANCZOS)
        keyed = Image.new("RGBA", (512, 512), (0, 0, 0, 0))
        keyed.alpha_composite(subject, (round(256 - subject.width / 2), 500 - subject.height))
        keyframe_file = keyframes / f"frame_{index:02d}.png"
        decontaminate_dark_outline(keyed).save(keyframe_file, optimize=True)
        record.update({"frame": index, "sourceFrame": order[index], "runtimeSha256": digest(runtime_file), "keyframeSha256": digest(keyframe_file)})
        frames.append(record)
    result = {
        "action": action, "sourceImage": source.relative_to(CHARACTER_ROOT).as_posix(),
        "sourceSha256": receipt["outputSha256"], "originalSha256": ORIGINAL_SHA256,
        "processor": "process_feibijiubi_pranks.py", "frameCount": 16, "frames": frames,
    }
    write_json(PREVIEW_ROOT / f"{action}_processing.json", result)
    return result


def validate_point(point: dict) -> None:
    if set(point) != {"x", "y"} or any(not isinstance(point[key], (int, float)) or not 0 <= point[key] <= 512 for key in ("x", "y")):
        raise ValueError("Anchors must contain finite x/y coordinates within the logical 512 canvas")


def validate_track(track: list, label: str) -> None:
    if len(track) != 16:
        raise ValueError(f"{label} must supply all 16 actual-frame anchors")
    for point in track:
        validate_point(point)


def crop_foreground(frame: Image.Image, polygon: list[dict]) -> Image.Image:
    if len(polygon) < 3:
        raise ValueError("The front hat brim needs at least 3 measured polygon points")
    for point in polygon:
        validate_point(point)
    mask = Image.new("L", frame.size, 0)
    ImageDraw.Draw(mask).polygon([(round(p["x"] * 0.75), round(p["y"] * 0.75)) for p in polygon], fill=255)
    rgba = np.array(frame.convert("RGBA"))
    rgba[:, :, 3] = np.minimum(rgba[:, :, 3], np.asarray(mask))
    rgba[rgba[:, :, 3] == 0, :3] = 0
    return Image.fromarray(rgba)


def publish_metadata(annotation_file: Path) -> dict:
    """Publish only measured, visually reviewed geometry matching current frame hashes."""
    annotations = json.loads(annotation_file.read_text(encoding="utf-8"))
    if annotations.get("originalSha256") != ORIGINAL_SHA256:
        raise ValueError("Measured geometry must identify the original reference")
    clips = {}
    for action in ACTIONS:
        measured = annotations["clips"][action]
        frames = [CHARACTER_ROOT / "runtime" / action / f"frame_{index:02d}.png" for index in range(16)]
        if [digest(frame) for frame in frames] != measured["runtimeSha256"]:
            raise ValueError(f"{action}: measured anchors belong to different runtime frames")
        visual_qa = measured.get("visualQa", {})
        for key in ("completeCharacter", "continuousMotion", "leftFacingInteraction", "correctHatState"):
            if visual_qa.get(key) is not True:
                raise ValueError(f"{action}: missing visual check {key}")
        clip = {"frameSeconds": FRAME_SECONDS[action], "frameCount": 16, "loop": action in LOOPS, "runtimeSha256": measured["runtimeSha256"]}
        if action in CONTACT_ACTIONS:
            validate_track(measured["contact"], f"{action}.contact")
            contact_frame = measured["contactFrame"]
            if not isinstance(contact_frame, int) or not 0 <= contact_frame < 16:
                raise ValueError("Invalid contact frame")
            clip.update({"contact": measured["contact"], "contactFrame": contact_frame})
        if "tearUpperHand" in measured:
            for key in ("tearUpperHand", "tearLowerHand"):
                validate_track(measured[key], f"{action}.{key}")
                clip[key] = measured[key]
        if action in HAT_ACTIONS:
            validate_track(measured["hatMouth"], f"{action}.hatMouth")
            widths = measured["hatWidth"]
            if len(widths) != 16 or any(not isinstance(width, (int, float)) or not 5 <= width <= 512 for width in widths):
                raise ValueError("Hat widths must be 16 measured positive canvas widths")
            polygons = measured["foregroundPolygons"]
            if len(polygons) != 16:
                raise ValueError("All 16 front-brim occlusion polygons are required")
            mask_folder = CHARACTER_ROOT / "runtime-masks" / action
            mask_folder.mkdir(parents=True, exist_ok=True)
            for index, (file, polygon) in enumerate(zip(frames, polygons, strict=True)):
                foreground = crop_foreground(Image.open(file).convert("RGBA"), polygon)
                if foreground.getbbox() is None:
                    raise ValueError(f"{action}/{index}: measured foreground brim is empty")
                foreground.save(mask_folder / f"frame_{index:02d}.png", optimize=True)
            clip.update({"hatMouth": measured["hatMouth"], "hatWidth": widths, "foregroundMaskFolder": f"runtime-masks/{action}"})
        if action in {"hat_throw", "hat_pickup"}:
            expected = 7 if action == "hat_throw" else 4
            if measured.get("hatTransferFrame") != expected:
                raise ValueError(f"{action}: hat handoff must match the agreed timeline")
            validate_point(measured["hatTransferPoint"])
            transfer_width = measured["hatTransferWidth"]
            if not isinstance(transfer_width, (int, float)) or not 5 <= transfer_width <= 512:
                raise ValueError("Hat handoff width must be the measured visible width")
            source_frame = 6 if action == "hat_throw" else 4
            if measured.get("hatTransferSourceFrame") != source_frame:
                raise ValueError("Hat transfer geometry must reference the actual visible hat frame")
            rotation = measured["hatTransferRotation"]
            if not isinstance(rotation, (int, float)) or not -360 <= rotation <= 360:
                raise ValueError("Hat transfer rotation must be measured screen degrees")
            clip.update({"hatTransferFrame": expected, "hatTransferSourceFrame": source_frame,
                         "hatTransferPoint": measured["hatTransferPoint"], "hatTransferWidth": transfer_width,
                         "hatTransferRotation": rotation})
        clips[action] = clip
    prop = CHARACTER_ROOT / "props" / "hat.png"
    if not prop.exists() or digest(prop) != annotations["hatPropSha256"]:
        raise ValueError("The original-derived independent hat prop must be visually approved")
    result = {
        "version": 1, "canvasSize": 512, "facing": "left", "clips": clips,
        "hatProp": "props/hat.png", "hatPropSha256": annotations["hatPropSha256"], "originalSha256": ORIGINAL_SHA256,
        "annotationFile": annotation_file.relative_to(CHARACTER_ROOT).as_posix(),
        "annotationSha256": digest(annotation_file),
    }
    write_json(CHARACTER_ROOT / "prank-animations.json", result)
    return result


def process_prop(source_root: Path, index: int) -> None:
    source, receipt = checked_receipt(source_root, "hat_prop")
    extracted = extract_characters(Image.open(source).convert("RGBA"), "hat_prop")
    subject, record = extracted[index]
    target_width = 280
    subject = subject.resize((target_width, round(subject.height * target_width / subject.width)), Image.Resampling.LANCZOS)
    frame = Image.new("RGBA", (384, 384), (0, 0, 0, 0))
    frame.alpha_composite(subject, (round(192 - subject.width / 2), round(192 - subject.height / 2)))
    frame = decontaminate_dark_outline(remove_saturated_chroma_spill(frame))
    props = CHARACTER_ROOT / "props"
    props.mkdir(parents=True, exist_ok=True)
    frame.save(props / "hat.png", optimize=True)
    write_json(PREVIEW_ROOT / "hat_prop_processing.json", {"sourceImage": source.relative_to(CHARACTER_ROOT).as_posix(), "sourceSha256": receipt["outputSha256"], "originalSha256": ORIGINAL_SHA256, "sourceFrame": index, "runtimeSha256": digest(props / "hat.png"), **record})


def audit_available() -> dict:
    ready = [action for action in ACTIONS if (PREVIEW_ROOT / f"{action}_processing.json").exists()]
    results = [audit_action(CHARACTER_ROOT / "runtime", PREVIEW_ROOT, action, playback_indices=tuple(range(16)), playback_duration_ms=round(FRAME_SECONDS[action] * 1000), playback_loop=action in LOOPS) for action in ready]
    for result in results:
        if result["action"] == "hat_throw":
            # Moving/removing the broad hat deliberately moves alpha mass even if
            # the girl's head stays steady, so its face anchor is the motion metric.
            centers = []
            for index in range(16):
                bounds = face_bounds(Image.open(CHARACTER_ROOT / "runtime" / "hat_throw" / f"frame_{index:02d}.png").convert("RGBA"))
                centers.append(np.array([(bounds[0] + bounds[2]) / 2, (bounds[1] + bounds[3]) / 2]))
            maximum = max(float(np.linalg.norm(a - b)) for a, b in zip(centers[1:], centers[:-1]))
            result["silhouetteCentroidMotion"] = result["maximumAdjacentCentroidMotion"]
            result["continuityMetric"] = "face-anchor; broad-hat alpha mass intentionally moves during lift and release"
            result["maximumAdjacentCentroidMotion"] = round(maximum, 3)
            result["continuityPassed"] = maximum <= result["continuityLimit"]
            result["passedDeterministicChecks"] = result["completeSilhouettes"] and result["continuityPassed"] and all(
                frame["size"] == [384, 384] and frame["meaningfulComponents"] == 1 and frame["magentaSpillPixels"] <= 12
                and frame["cornersClear"] and frame["bbox"][0] >= 10 and frame["bbox"][1] >= 10
                and frame["bbox"][2] <= 374 and frame["bbox"][3] <= 374 for frame in result["frames"])
    for action in ready:
        contact = Image.new("RGB", (512, 512), (246, 247, 249))
        for index in range(16):
            frame = Image.open(CHARACTER_ROOT / "runtime" / action / f"frame_{index:02d}.png").convert("RGBA")
            contact.paste(composite_preview(frame, 128), ((index % 4) * 128, (index // 4) * 128))
        contact.save(PREVIEW_ROOT / f"{action}_128_contact.png", optimize=True)
    if ready:
        tile, header, columns = 384, 32, 4
        overview = Image.new("RGB", (columns * tile, ((len(ready) + 3) // 4) * (tile + header)), (246, 247, 249))
        draw = ImageDraw.Draw(overview)
        try:
            font = ImageFont.truetype("C:/Windows/Fonts/arial.ttf", 21)
        except OSError:
            font = ImageFont.load_default()
        for index, action in enumerate(ready):
            x, y = (index % columns) * tile, (index // columns) * (tile + header)
            contact = Image.open(PREVIEW_ROOT / f"{action}_128_contact.png").resize((tile, tile), Image.Resampling.LANCZOS)
            overview.paste(contact, (x, y + header))
            draw.text((x + 10, y + 3), action, fill=(40, 45, 60), font=font)
        overview.save(PREVIEW_ROOT / "overview.png", optimize=True)
    result = {"expectedActionCount": len(ACTIONS), "actionCount": len(ready), "frameCount": len(ready) * 16, "complete": len(ready) == len(ACTIONS), "allPassed": bool(ready) and all(item["passedDeterministicChecks"] for item in results), "actions": results, "visualAcceptance": "Requires actual frame review and matching measured annotation hashes before metadata publication"}
    write_json(PREVIEW_ROOT / "audit.json", result)
    return result


def self_test() -> None:
    assert len(ACTIONS) == 20 and len(set(ACTIONS)) == 20
    assert set(FRAME_SECONDS) == set(ACTIONS)
    validate_track([{"x": 100, "y": 300}] * 16, "test")
    for invalid in ([{"x": 100, "y": 300}] * 15, [{"x": float("nan"), "y": 0}] * 16):
        try:
            validate_track(invalid, "invalid")
        except ValueError:
            pass
        else:
            raise AssertionError("Invalid animation anchors must be rejected")
    original_frame = Image.open(CHARACTER_ROOT / "runtime" / "idle" / "frame_00.png").convert("RGBA")
    bounds = face_bounds(original_frame)
    assert bounds[2] > bounds[0] and bounds[3] > bounds[1]
    # In-memory mask checks are diagnostics, not art or runtime placeholders.
    masked = crop_foreground(original_frame, [{"x": 0, "y": 0}, {"x": 512, "y": 0}, {"x": 512, "y": 256}, {"x": 0, "y": 256}])
    assert not np.asarray(masked)[193:, :, 3].any()
    assert np.array_equal(np.asarray(masked)[:192, :, 3], np.asarray(original_frame)[:192, :, 3])
    assert digest(CHARACTER_ROOT / "original.png") == ORIGINAL_SHA256
    print(json.dumps({"selfTestPassed": True, "tests": 7, "imagesGenerated": 0, "runtimeAssetsWritten": 0}))


def audit_provenance() -> dict:
    manifest_file = CHARACTER_ROOT / "prank-animations.json"
    manifest = json.loads(manifest_file.read_text(encoding="utf-8"))
    receipts = []
    for receipt_file in sorted(CHARACTER_ROOT.glob("source-pranks-*/*_generation.json")):
        action = receipt_file.name.removesuffix("_generation.json")
        _, receipt = checked_receipt(receipt_file.parent, action)
        if receipt["sourceImage"] != "original.png":
            raise ValueError("A generated image must never be used as a model input")
        receipts.append({key: receipt.get(key) for key in (
            "action", "model", "transport",
            "sourceImage", "sourceSha256", "prompt", "promptSha256", "image", "outputSha256")})
    selected, mask_count = {}, 0
    for action in ACTIONS:
        record = json.loads((PREVIEW_ROOT / f"{action}_processing.json").read_text(encoding="utf-8"))
        if record["originalSha256"] != ORIGINAL_SHA256 or digest(CHARACTER_ROOT / record["sourceImage"]) != record["sourceSha256"]:
            raise ValueError(f"{action}: selected original-derived source changed")
        if [frame["sourceFrame"] for frame in record["frames"]] != list(range(16)):
            raise ValueError(f"{action}: preserve all 16 native source frames in order")
        hashes = []
        for index, frame_record in enumerate(record["frames"]):
            file = CHARACTER_ROOT / "runtime" / action / f"frame_{index:02d}.png"
            frame_hash = digest(file)
            if frame_hash != frame_record["runtimeSha256"] or frame_hash != manifest["clips"][action]["runtimeSha256"][index]:
                raise ValueError(f"{action}/{index}: runtime hash does not match its provenance and manifest")
            hashes.append(frame_hash)
            if action in HAT_ACTIONS:
                original = np.asarray(Image.open(file).convert("RGBA"))
                mask = np.asarray(Image.open(CHARACTER_ROOT / "runtime-masks" / action / f"frame_{index:02d}.png").convert("RGBA"))
                visible = mask[:, :, 3] > 0
                if not visible.any() or visible.sum() >= (original[:, :, 3] > 0).sum() or not np.array_equal(mask[visible], original[visible]):
                    raise ValueError(f"{action}/{index}: foreground must be a nonempty exact runtime pixel subset")
                mask_count += 1
        selected[action] = {"sourceImage": record["sourceImage"], "sourceSha256": record["sourceSha256"],
                            "nativeSourceFrameOrder": list(range(16)), "runtimeSha256": hashes}
    prop = json.loads((PREVIEW_ROOT / "hat_prop_processing.json").read_text(encoding="utf-8"))
    if digest(CHARACTER_ROOT / "props" / "hat.png") != prop["runtimeSha256"] or prop["runtimeSha256"] != manifest["hatPropSha256"]:
        raise ValueError("Independent hat prop hash differs from the approved source and runtime manifest")
    selected["hat_prop"] = {key: prop[key] for key in ("sourceImage", "sourceSha256", "sourceFrame", "runtimeSha256")}
    result = {"version": 1, "originalSha256": ORIGINAL_SHA256, "allPassed": True,
              "generatedSheetCount": len(receipts), "selectedSheetCount": len(selected), "clipCount": len(ACTIONS),
              "runtimeFrameCount": len(ACTIONS)*16, "foregroundMaskCount": mask_count,
              "lineagePolicy": "original-image-only; no generated image has been used as a model input",
              "manifestSha256": digest(manifest_file), "selectedSources": selected, "generationReceipts": receipts}
    write_json(CHARACTER_ROOT / "source-selections-pranks.json", result)
    return result


def main() -> None:
    parser = argparse.ArgumentParser(description="Cut new original-derived Feibi prank sheets; publish only visually measured geometry.")
    parser.add_argument("--source-root", default="source-pranks-v1")
    parser.add_argument("--actions", nargs="+", choices=ACTIONS)
    parser.add_argument("--audit-only", action="store_true")
    parser.add_argument("--publish-annotations", help="Reviewed JSON path relative to the character asset directory")
    parser.add_argument("--hat-prop-frame", type=int, choices=range(16))
    parser.add_argument("--self-test", action="store_true")
    args = parser.parse_args()
    if digest(CHARACTER_ROOT / "original.png") != ORIGINAL_SHA256:
        raise ValueError("Original reference SHA-256 mismatch")
    if args.self_test:
        self_test()
        return
    source_root = inside_character(CHARACTER_ROOT / args.source_root)
    failures = []
    if not args.audit_only and not args.publish_annotations:
        for action in args.actions or ACTIONS:
            try:
                process_action(source_root, action)
                print(f"Processed: {action}")
            except (ValueError, FileNotFoundError) as error:
                failures.append(str(error))
        if args.hat_prop_frame is not None:
            process_prop(source_root, args.hat_prop_frame)
    if args.publish_annotations:
        publish_metadata(inside_character(CHARACTER_ROOT / args.publish_annotations))
    report = audit_available()
    if args.publish_annotations and report["complete"] and report["allPassed"]:
        provenance = audit_provenance()
        print(json.dumps({"provenancePassed": provenance["allPassed"], "generatedSheets": provenance["generatedSheetCount"],
                          "selectedSheets": provenance["selectedSheetCount"], "foregroundMasks": provenance["foregroundMaskCount"]}))
    print(json.dumps({"complete": report["complete"], "allPassed": report["allPassed"], "runtimeFrames": report["frameCount"], "failures": failures}))
    if failures or not report["allPassed"]:
        raise SystemExit(2)


if __name__ == "__main__":
    main()
