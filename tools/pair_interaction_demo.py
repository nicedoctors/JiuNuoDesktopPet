"""Headless, silent preview of the three actual paired runtime sprite clips."""

from __future__ import annotations

import math
import json
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont


ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "dist" / "双宠互动演示"
NUONUO = ROOT / "assets" / "sprites" / "runtime"
FEIBI = ROOT / "assets" / "characters" / "feibijiubi" / "runtime"
PROPS = ROOT / "assets" / "pair_interactions" / "props"
W, H, FPS, GROUND, SIZE = 960, 540, 15, 466, 310
FONT = ImageFont.truetype("C:/Windows/Fonts/msyh.ttc", 23)
SMALL = ImageFont.truetype("C:/Windows/Fonts/msyh.ttc", 16)
FRAME_CACHE = {}
CALIBRATION = json.loads((ROOT/'assets/pair_interactions/calibration.json').read_text(encoding='utf-8'))


def clamp(value):
    return max(0, min(1, value))


def ease(value):
    x = clamp(value)
    return x*x*(3-2*x)


def sprite(character, action, index):
    key = (character, action, index)
    if key not in FRAME_CACHE:
        root = FEIBI if character == "feibi" else NUONUO
        image = Image.open(root / action / f"frame_{index:02d}.png").convert("RGBA")
        FRAME_CACHE[key] = image.resize((SIZE, SIZE), Image.Resampling.LANCZOS)
    return FRAME_CACHE[key]


def base(title, subtitle):
    image = Image.new("RGBA", (W, H), "#FAF6F2")
    draw = ImageDraw.Draw(image)
    draw.rectangle((0, GROUND, W, H), fill="#E7DBCE")
    draw.line((0, GROUND, W, GROUND), fill="#C9B7A8", width=2)
    draw.rounded_rectangle((22, 18, 916, 81), radius=15, fill="#FFFFFF", outline="#E5D9CE", width=2)
    draw.text((42, 26), title, font=FONT, fill="#5B4D57")
    draw.text((390, 31), subtitle, font=SMALL, fill="#8D7A83")
    return image


def draw_pet(image, character, action, frame, x):
    image.alpha_composite(sprite(character, action, frame),
                          (round(x-SIZE/2), GROUND-SIZE))


def food(image, name, x, y, scale):
    icon = Image.open(PROPS / f"{name}.png").convert("RGBA")
    size = max(8, round(60*scale))
    icon = icon.resize((size, size), Image.Resampling.LANCZOS)
    image.alpha_composite(icon, (round(x-size/2), round(y-size/2)))


def scene_frame(kind, t, treat="cake"):
    labels = {
        "cheek": ("菲比啾比揪糯糯的脸", "靠近 → 轻揪 → Q弹回弹", 2.7, .145, 205),
        "sleep": ("两个人靠在一起睡觉", "打哈欠 → 挨近 → 靠着呼呼睡", 8.2, .165, 180),
        "feed": ("摘帽抛食，糯糯张嘴接住", f"远距离抛物线 · 本次食物：{treat}", 6.4, .175, 420),
    }
    title, subtitle, duration, seconds, gap = labels[kind]
    image = base(title, subtitle)
    approach = ease(t/.85)
    nx = 255 + (480-gap/2-255)*approach
    fx = 770 + (480+gap/2-770)*approach
    if t < .85:
        n_action = f_action = "run"
        index = int(t/.075)%16
        n_index = f_index = index
    else:
        action_time = t-.85
        if kind == "sleep":
            n_action = f_action = "pair_sleep"
            if action_time < 2.64:
                n_index = f_index = min(15, int(action_time/seconds))
            else:
                hold = int((action_time-2.64)/.32)%10
                n_index = f_index = 10 + (hold if hold <= 5 else 10-hold)
        elif kind == "feed":
            if action_time < 1.76:
                f_action, f_index = "hat_open", min(15, int(action_time/.110))
            elif action_time < 4.56:
                f_action, f_index = "pair_feed", min(15, int((action_time-1.76)/seconds))
            else:
                f_action, f_index = "hat_wear", min(15, int((action_time-4.56)/.110))
            if action_time < 3.25:
                n_action = "pair_feed"
                n_index = (0, 1, 2, 1)[int(action_time/.4)%4]
            elif action_time < 4.05:
                n_action = "pair_feed"
                n_index = 3+min(2, int((action_time-3.25)/.25))
            else:
                n_action, n_index = "pair_feed", min(15, 6+int((action_time-4.05)/seconds))
        else:
            n_action = f_action = "pair_cheek" if kind == "cheek" else "pair_feed"
            n_index = f_index = min(15, int(action_time/seconds))
    draw_pet(image, "nuonuo", n_action, n_index, nx)
    draw_pet(image, "feibi", f_action, f_index, fx)
    if kind == "feed" and t >= .85:
        a = t-.85
        if 2.78 <= a < 4.71:
            def project(center, character, frame, cx, cy):
                transform = CALIBRATION[f'{character}/pair_feed'][frame]
                cx,cy = cx*transform['Scale']+transform['X'], cy*transform['Scale']+transform['Y']
                return center+(cx-256)*SIZE/512, GROUND-SIZE+cy*SIZE/512
            hat = project(fx, 'feibijiubi', 6, 256, 388)
            hand = project(fx, 'feibijiubi', 9, 112, 255)
            release = project(fx, 'feibijiubi', 12, 95, 310)
            mouth = project(nx, 'nuonuo', 9, 290, 240)
            if a < 3.45:
                p = ease((a-2.78)/.67)
                x = hat[0]+(hand[0]-hat[0])*p
                y = hat[1]+(hand[1]-hat[1])*p-10*math.sin(p*math.pi)
                scale = .28+.72*p
            elif a < 3.86:
                p = ease((a-3.45)/(3.86-3.45))
                x = hand[0]+(release[0]-hand[0])*p
                y = hand[1]+(release[1]-hand[1])*p
                scale = 1
            else:
                p = (a-3.86)/(4.71-3.86)
                x = release[0]+(mouth[0]-release[0])*p
                y = release[1]+(mouth[1]-release[1])*p-62*math.sin(p*math.pi)
                scale = 1-.42*p
            food(image, treat, x, y, scale)
    return image.convert("RGB")


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    descriptions = [("cheek", 3.55), ("sleep", 9.05), ("feed", 7.25)]
    storyboard = Image.new("RGB", (W*3, H*3), "#FFFFFF")
    for row, (kind, total) in enumerate(descriptions):
        frames = [scene_frame(kind, min(total, i/FPS), "cake")
                  for i in range(round(total*FPS)+1)]
        target = OUT / f"{row+1}-{kind}.gif"
        frames[0].save(target, save_all=True, append_images=frames[1:],
                       duration=round(1000/FPS), loop=0, optimize=True)
        snapshots = (2.65, 4.6, 5.3) if kind == "feed" else tuple(total*f for f in (.16, .52, .82))
        for col, snapshot in enumerate(snapshots):
            storyboard.paste(scene_frame(kind, snapshot), (col*W, row*H))
        print(target)
    target = OUT / "三段互动分镜.png"
    storyboard.save(target, optimize=True)
    print(target)
    feed_moments = (2.1, 2.95, 3.75, 4.48, 5.0, 5.51, 6.15, 6.95)
    feed_board = Image.new("RGB", (W*4, H*2), "#FFFFFF")
    for index, moment in enumerate(feed_moments):
        feed_board.paste(scene_frame("feed", moment), ((index%4)*W, (index//4)*H))
    target = OUT / "投喂八格分镜.png"
    feed_board.save(target, optimize=True)
    print(target)
    target = OUT / "投喂接食瞬间.png"
    scene_frame("feed", 5.51).save(target, optimize=True)
    print(target)


if __name__ == "__main__":
    main()
