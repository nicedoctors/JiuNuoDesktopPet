"""Draw four tiny, deterministic snack props for the paired feeding scene."""

from pathlib import Path
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[1] / "assets" / "pair_interactions" / "props"
ROOT.mkdir(parents=True, exist_ok=True)
S = 4


def box(coords):
    return tuple(round(value * S) for value in coords)


def save(name, draw_icon):
    image = Image.new("RGBA", (128 * S, 128 * S))
    draw_icon(ImageDraw.Draw(image))
    image.resize((128, 128), Image.Resampling.LANCZOS).save(ROOT / f"{name}.png")


def apple(d):
    d.ellipse(box((18, 35, 72, 111)), fill="#F45D63", outline="#842E42", width=4*S)
    d.ellipse(box((55, 35, 110, 111)), fill="#E93751", outline="#842E42", width=4*S)
    d.polygon([box((46, 43))[0:2], box((63, 50))[0:2], box((81, 43))[0:2]], fill="#F25660")
    d.arc(box((17, 31, 111, 111)), 0, 180, fill="#842E42", width=4*S)
    d.line(box((65, 39, 66, 19)), fill="#603C30", width=5*S)
    d.ellipse(box((64, 13, 94, 32)), fill="#75C780", outline="#3D8665", width=3*S)
    d.ellipse(box((32, 47, 44, 69)), fill="#FFB2B1")


def cake(d):
    d.rounded_rectangle(box((16, 48, 111, 108)), radius=10*S, fill="#FFD6A9", outline="#94566C", width=4*S)
    d.rectangle(box((20, 68, 107, 79)), fill="#F688AB")
    d.rounded_rectangle(box((14, 36, 113, 60)), radius=9*S, fill="#FFF9EE", outline="#94566C", width=4*S)
    for x in (34, 58, 84):
        d.ellipse(box((x, 52, x+13, 72)), fill="#FFF9EE")
    d.ellipse(box((47, 17, 81, 45)), fill="#F04461", outline="#9B3152", width=3*S)
    d.ellipse(box((55, 20, 65, 28)), fill="#FFE8E8")
    d.polygon([(80*S, 29*S), (92*S, 20*S), (94*S, 30*S)], fill="#5BBE7C")


def donut(d):
    d.ellipse(box((17, 23, 111, 112)), fill="#D89066", outline="#85506D", width=4*S)
    d.ellipse(box((23, 27, 105, 91)), fill="#FFAED3", outline="#A95A88", width=3*S)
    d.ellipse(box((50, 51, 77, 78)), fill=(0,0,0,0), outline="#A95A88", width=4*S)
    d.ellipse(box((52, 54, 75, 76)), fill=(0,0,0,0))
    for x,y,color in [(36,44,"#FFF8B0"),(78,40,"#9AE2FD"),(35,70,"#B9FCBD"),(87,70,"#FFF8B0"),(61,87,"#FFFFFF")]:
        d.line(box((x,y,x+8,y-3)), fill=color, width=3*S)


def pudding(d):
    d.rounded_rectangle(box((19, 80, 110, 110)), radius=10*S, fill="#F7F9FF", outline="#6E6381", width=4*S)
    d.polygon([(36*S,37*S),(91*S,37*S),(101*S,87*S),(26*S,87*S)], fill="#FFDB82")
    d.arc(box((25,36,102,90)),0,180,fill="#8C5B69",width=4*S)
    d.ellipse(box((35,22,93,52)), fill="#A96552", outline="#784D58", width=4*S)
    d.ellipse(box((46,26,65,34)), fill="#E6AB91")
    d.ellipse(box((45,60,54,73)), fill="#A5665F")
    d.ellipse(box((75,60,84,73)), fill="#A5665F")


if __name__ == "__main__":
    for name, factory in (("apple", apple), ("cake", cake), ("donut", donut), ("pudding", pudding)):
        save(name, factory)
