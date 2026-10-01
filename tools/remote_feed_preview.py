"""Offline art review of the production feed flight and animator trace. No desktop capture."""
import json
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

ROOT=Path(__file__).resolve().parents[1]
OUT=ROOT/'dist/双宠互动演示'
FONT=ImageFont.truetype('C:/Windows/Fonts/msyh.ttc',22)
cache={}

def cel(character,folder,index,size,mirror):
    key=(character,folder,index,size,mirror)
    if key not in cache:
        root=ROOT/'assets/sprites/runtime' if character=='nuonuo' else ROOT/'assets/characters/feibijiubi/runtime'
        image=Image.open(root/folder/f'frame_{index:02}.png').convert('RGBA').resize((size,size),Image.Resampling.LANCZOS)
        cache[key]=image.transpose(Image.Transpose.FLIP_LEFT_RIGHT) if mirror else image
    return cache[key]

def main():
    OUT.mkdir(parents=True,exist_ok=True)
    treat=Image.open(ROOT/'assets/pair_interactions/props/cake.png').convert('RGBA')
    for file in sorted((ROOT/'.codex-temp/feed-preview').glob('*.json')):
        trace=json.loads(file.read_text(encoding='utf-8'))
        frames=[]
        for frame in trace['frames']:
            image=Image.new('RGBA',(1280,570),'#FAF6F2')
            draw=ImageDraw.Draw(image)
            draw.text((24,20),'原地抛食 · 两人保持距离' if file.stem=='long' else '反向站位 · 高低差抛接',font=FONT,fill='#594957')
            draw.text((24,54),f"离线预览 / 抵达嘴部 {trace['arrival']:.2f}s",font=FONT,fill='#8A7380')
            for char,x,y,folder,index in [('nuonuo',trace['nx'],trace['ny'],'pair_feed',frame['nFrame']),
                ('feibijiubi',trace['fx'],trace['fy'],frame['fFolder'],frame['fFrame'])]:
                draw.line((x-15,y+trace['size']-7,x+trace['size']+15,y+trace['size']-7),fill='#D2BBA8',width=3)
                art=cel(char,folder,index,int(trace['size']),trace['facing']==-1)
                image.alpha_composite(art,(round(x),round(y)))
            if frame['scale']>0:
                size=max(2,round(trace['size']*.23*frame['scale']))
                prop=treat.resize((size,size),Image.Resampling.LANCZOS).rotate(frame['spin'],resample=Image.Resampling.BICUBIC,expand=True)
                image.alpha_composite(prop,(round(frame['x']-prop.width/2),round(frame['y']-prop.height/2)))
            frames.append(image.convert('RGB'))
        target=OUT/f'原地抛食-{file.stem}.gif'
        frames[0].save(target,save_all=True,append_images=frames[1:],duration=[33]*len(frames),loop=0)
        frames[round(trace['arrival']*30)-2].save(OUT/f'原地抛食-{file.stem}-catch.png')
        print(target)

if __name__=='__main__':main()
