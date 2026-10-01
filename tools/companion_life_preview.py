"""Silent offscreen review of production companion sprites; never launches the pet."""
from pathlib import Path
import json
import math
from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "dist" / "双宠日常互动预览"
TRACE = ROOT / ".codex-temp/companion-life/ball-trace.json"
W, H, SIZE, GROUND, FPS = 1080, 580, 240, 500, 20
FONT = ImageFont.truetype("C:/Windows/Fonts/msyh.ttc", 22)
SMALL = ImageFont.truetype("C:/Windows/Fonts/msyh.ttc", 15)
CACHE = {}
RUNTIME = {"n": ROOT / "assets/sprites/runtime",
           "f": ROOT / "assets/characters/feibijiubi/runtime"}
LABELS = {"follow": "小跟屁虫", "nuzzle": "路过蹭一下",
          "ball": "一起玩小球", "watch": "一起看热闹"}


def ease(t):
    t = max(0, min(1, t))
    return t*t*(3-2*t)


def pet(image, who, clip, frame, x, flip=False):
    key = (who, clip, max(0, min(15, frame)), flip)
    if key not in CACHE:
        sprite = Image.open(RUNTIME[who] / clip / f"frame_{key[2]:02d}.png").convert("RGBA")
        sprite = sprite.resize((SIZE, SIZE), Image.Resampling.LANCZOS)
        if flip:
            sprite = sprite.transpose(Image.Transpose.FLIP_LEFT_RIGHT)
        CACHE[key] = sprite
    image.alpha_composite(CACHE[key], (round(x-SIZE/2), round(GROUND-374/384*SIZE)))


def base(kind, t):
    im = Image.new("RGBA", (W, H), "#faf6f2")
    d = ImageDraw.Draw(im)
    d.rounded_rectangle((24, 18, W-24, 84), radius=16, fill="white", outline="#e4d7cc", width=2)
    d.text((42, 30), LABELS[kind], font=FONT, fill="#5b4d57")
    d.text((270, 37), f"运行素材离屏预览 · 无声 · {t:.1f}s", font=SMALL, fill="#8d7a83")
    d.rectangle((0, GROUND, W, H), fill="#e7dbce")
    return im


def frame(kind, t, trace=None):
    im = base(kind, t)
    if kind == "ball":
        pet(im, "n", trace["n"], trace["nf"], 260)
        pet(im, "f", trace["f"], trace["ff"], 800)
        if trace["Opacity"] > 0:
            ball = Image.new("RGBA", (50,50))
            d = ImageDraw.Draw(ball)
            d.ellipse((5,5,45,45), fill="#ffbd85", outline="#573f58", width=2)
            d.rounded_rectangle((8,22,42,30), radius=4, fill="#97d0ed")
            d.ellipse((14,12,25,18), fill="#fff2e7")
            ball = ball.rotate(-t*170, resample=Image.Resampling.BICUBIC)
            ball.putalpha(ball.getchannel("A").point(lambda a:round(a*trace["Opacity"])))
            im.alpha_composite(ball, (round(trace["X"]-25),round(trace["Y"]-25)))
    elif kind == "follow":
        fp = .55*ease(t/2.4) if t < 2.4 else .55 if t < 3.4 else .55+.45*ease((t-3.4)/2.6)
        np = ease((t-.8)/5.7)
        nx, fx = 230+(860-.85*252-230)*np, 500+360*fp
        if 2.4 <= t < 3.4 or t >= 6:
            pet(im,"f","pair_notice",int(((t-2.4)*1.3 if t<3.4 else t-6)/.18),fx)
        else:
            pet(im,"f","walk",int((t if t<2.4 else t-3.4)/.094)%16,fx)
        if t<.8 or t>=6.5:
            pet(im,"n","pair_notice",int((t if t<.8 else t-6.5)/.18),nx)
        else:
            pet(im,"n","run",int((t-.8)/.07)%16,nx)
    elif kind == "nuzzle":
        u=ease(t/1.2)
        nx,fx=350+(477-350)*u, 730+(603-730)*u
        if t<1.2:
            pet(im,"n","walk",int(t/.11)%16,nx)
            pet(im,"f","walk",int(t/.094)%16,fx,True)
        else:
            pet(im,"n","pair_nuzzle",int(max(0,t-1.38)/.2),nx)
            pet(im,"f","pair_nuzzle",int((t-1.2)/.2),fx)
    else:
        d=ImageDraw.Draw(im)
        d.rounded_rectangle((750,140,1020,240),radius=10, fill="#e1eff9",outline="#a6bcd0",width=2)
        d.text((782,171),"附近出现了新窗口",font=SMALL,fill="#56748e")
        pet(im,"n","pair_notice",int(max(0,t-.55)/.18),405)
        pet(im,"f","pair_notice",int(t/.18),620,True)
    return im.convert("RGB")


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    traces = json.loads(TRACE.read_text(encoding="utf-8"))
    durations = {"follow":8.2, "nuzzle":4.8, "ball":21, "watch":4.2}
    contact = Image.new("RGB",(W, 4*H),"white")
    for row,(kind,duration) in enumerate(durations.items()):
        frames = [frame(kind,i/FPS,traces[i] if kind=="ball" else None)
                  for i in range(round(duration*FPS)+1)]
        frames[0].save(OUT/f"{LABELS[kind]}.gif",save_all=True,append_images=frames[1:],
                       duration=50,loop=0,optimize=False,disposal=2)
        chosen={"follow":3.1,"nuzzle":2.8,"ball":7.1,"watch":1.7}[kind]
        contact.paste(frames[round(chosen*FPS)],(0,row*H))
    contact.resize((810,1740),Image.Resampling.LANCZOS).save(OUT/"四种互动.png")
    print(OUT)


if __name__=="__main__":
    main()
