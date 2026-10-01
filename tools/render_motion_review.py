"""Compose offline PNGs produced by the actual C# renderer; no desktop capture."""
from pathlib import Path
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / '.codex-temp/pinch-preview'
TARGET = ROOT / 'docs/media/pinch-continuity-v131.gif'

frames = []
for i in range(105):
    canvas = Image.new('RGB',(500,270),'#f5f1ec')
    draw = ImageDraw.Draw(canvas)
    draw.text((16,12),'Left cheek / pull - hold - release / offline renderer', fill='#514c58')
    for x, name in [(20,'nuonuo'),(260,'feibijiubi')]:
        frame = Image.open(SOURCE/name/f'{i:03d}.png').convert('RGBA').resize((220,220),Image.Resampling.LANCZOS)
        canvas.paste(frame,(x,35),frame)
    frames.append(canvas)
TARGET.parent.mkdir(parents=True,exist_ok=True)
frames[0].save(TARGET,save_all=True,append_images=frames[1:],duration=[30,30,40]*35,loop=0)
print(TARGET)
