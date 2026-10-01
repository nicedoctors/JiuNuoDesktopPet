from __future__ import annotations

import math
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont


ROOT = Path(__file__).resolve().parents[1]
NUONUO = ROOT / "assets" / "sprites" / "runtime"
FEIBI = ROOT / "assets" / "characters" / "feibijiubi" / "runtime"
OUT_DIR = ROOT / "dist"
OUT_DIR.mkdir(parents=True, exist_ok=True)

W, H = 1280, 720
GROUND = 590
FPS = 30
SCALE = 0.52
SPRITE_SIZE = int(384 * SCALE)


def font(size: int):
    candidates = [
        Path("C:/Windows/Fonts/msyh.ttc"),
        Path("C:/Windows/Fonts/msyhbd.ttc"),
        Path("C:/Windows/Fonts/simhei.ttf"),
    ]
    for candidate in candidates:
        if candidate.exists():
            return ImageFont.truetype(str(candidate), size)
    return ImageFont.load_default()


TITLE = font(28)
SUBTITLE = font(20)


def load_frame(root: Path, action: str, index: int) -> Image.Image:
    directory = root / action
    if not directory.exists():
        directory = root / "idle"
    files = sorted(directory.glob("frame_*.png"))
    if not files:
        files = sorted((root / "idle").glob("frame_*.png"))
    image = Image.open(files[index % len(files)]).convert("RGBA")
    image.thumbnail((SPRITE_SIZE, SPRITE_SIZE), Image.Resampling.LANCZOS)
    canvas = Image.new("RGBA", (SPRITE_SIZE, SPRITE_SIZE), (0, 0, 0, 0))
    canvas.alpha_composite(image, ((SPRITE_SIZE - image.width) // 2, SPRITE_SIZE - image.height))
    return canvas


def ease(value: float) -> float:
    value = max(0.0, min(1.0, value))
    return value * value * (3 - 2 * value)


def bob(frame: int, phase: float = 0.0, amount: float = 4.0) -> int:
    return round(math.sin(frame * 0.22 + phase) * amount)


def background(scene_title: str, scene_subtitle: str, frame: int) -> Image.Image:
    image = Image.new("RGBA", (W, H), (244, 238, 232, 255))
    draw = ImageDraw.Draw(image)
    for y in range(H):
        t = y / H
        color = (244 - round(t * 16), 238 - round(t * 12), 232 - round(t * 4), 255)
        draw.line((0, y, W, y), fill=color)
    draw.rectangle((0, GROUND, W, H), fill=(221, 204, 190, 255))
    draw.line((0, GROUND, W, GROUND), fill=(181, 157, 143, 255), width=3)
    draw.rounded_rectangle((38, 30, 650, 102), radius=22, fill=(255, 250, 246, 235), outline=(211, 183, 171, 255), width=2)
    draw.text((64, 45), scene_title, fill=(84, 62, 69, 255), font=TITLE)
    draw.text((66, 78), scene_subtitle, fill=(134, 108, 110, 255), font=SUBTITLE)
    # A subtle shared floor highlight keeps the two sprites grounded without adding a sprite shadow.
    draw.line((180, GROUND + 12, 1100, GROUND + 12), fill=(199, 176, 161, 210), width=2)
    return image


def paste_character(canvas: Image.Image, sprite: Image.Image, center_x: float, lift: int = 0):
    x = round(center_x - SPRITE_SIZE / 2)
    y = round(GROUND - SPRITE_SIZE + lift)
    canvas.alpha_composite(sprite, (x, y))


def food_card(canvas: Image.Image, x: float, y: float, scale: float):
    draw = ImageDraw.Draw(canvas)
    size = round(72 * scale)
    left = round(x - size / 2)
    top = round(y - size / 2)
    draw.rounded_rectangle((left, top, left + size, top + size), radius=max(8, size // 7), fill=(252, 252, 255, 255), outline=(117, 154, 211, 255), width=max(2, round(3 * scale)))
    draw.ellipse((left + size * .2, top + size * .16, left + size * .8, top + size * .76), fill=(93, 192, 244, 255), outline=(55, 125, 190, 255), width=max(1, round(2 * scale)))
    draw.ellipse((left + size * .35, top + size * .28, left + size * .51, top + size * .44), fill=(255, 255, 255, 220))


def scene_meet(frame: int) -> Image.Image:
    image = background("① 碰面问候", "菲比啾比先发现糯糯，靠近后轻轻打招呼", frame)
    progress = ease((frame - 4) / 52)
    feibi_x = 870 - 265 * progress
    nuonuo_x = 430 + 18 * ease((frame - 56) / 20)
    feibi_action = "run" if frame < 50 else "curious"
    nuonuo_action = "idle" if frame < 58 else "curious"
    feibi = load_frame(FEIBI, feibi_action, frame * 2)
    nuonuo = load_frame(NUONUO, nuonuo_action, frame * 2)
    paste_character(image, nuonuo, nuonuo_x, bob(frame, 0.4, 2.0))
    paste_character(image, feibi, feibi_x, bob(frame, 1.2, 3.0))
    if 59 <= frame < 77:
        draw = ImageDraw.Draw(image)
        draw.ellipse((round(nuonuo_x + 125), GROUND - 270, round(nuonuo_x + 140), GROUND - 255), fill=(255, 220, 229, 230))
        draw.ellipse((round(feibi_x - 145), GROUND - 298, round(feibi_x - 130), GROUND - 283), fill=(255, 231, 164, 230))
    return image


def scene_food(frame: int) -> Image.Image:
    image = background("⑤ 进食围观", "糯糯安心吃东西，菲比啾比在旁边好奇地围观", frame)
    local = frame - 90
    nuonuo_x = 490 + 5 * math.sin(local * .08)
    feibi_x = 820 + 5 * math.sin(local * .11 + 1.0)
    nuonuo_action = "chomp" if local < 50 else "satisfied"
    feibi_action = "curious" if local < 62 else "idle"
    nuonuo = load_frame(NUONUO, nuonuo_action, local * 2)
    feibi = load_frame(FEIBI, feibi_action, local * 2)
    paste_character(image, nuonuo, nuonuo_x, bob(local, 0.5, 2.0))
    paste_character(image, feibi, feibi_x, bob(local, 1.3, 3.0))
    if local < 50:
        p = ease(local / 46)
        food_x = 720 - 235 * p
        food_y = GROUND - 175 - 20 * math.sin(p * math.pi)
        food_card(image, food_x, food_y, 1.0 - .12 * p)
    elif local < 78:
        food_card(image, nuonuo_x + 4, GROUND - 185, 0.45)
        draw = ImageDraw.Draw(image)
        draw.ellipse((round(feibi_x - 145), GROUND - 300, round(feibi_x - 130), GROUND - 285), fill=(255, 231, 164, 230))
    return image


def make_frames() -> list[Image.Image]:
    # 3 seconds greeting + 4 seconds food reaction, joined without a hard cut.
    return [scene_meet(frame) if frame < 90 else scene_food(frame) for frame in range(210)]


def main():
    frames = make_frames()
    gif_path = OUT_DIR / "双宠互动演示-碰面与进食围观.gif"
    mp4_dir = OUT_DIR / "interaction-demo-frames"
    mp4_dir.mkdir(exist_ok=True)
    for index, frame in enumerate(frames):
        frame.convert("RGB").save(mp4_dir / f"frame_{index:04d}.png", optimize=True)
    frames[0].save(gif_path, save_all=True, append_images=frames[1:], duration=1000 // FPS, loop=0, disposal=2, optimize=False)
    print(gif_path)
    print(mp4_dir)


if __name__ == "__main__":
    main()
