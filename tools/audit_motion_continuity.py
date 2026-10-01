"""Offline contact sheets and face-scale measurements; never starts the pet."""
from pathlib import Path
import json
import numpy as np
from PIL import Image, ImageDraw
from process_feibijiubi_pranks import face_bounds

ROOT = Path(__file__).resolve().parents[1]
OUTPUT = ROOT / '.codex-temp' / 'motion-review'

def main():
    OUTPUT.mkdir(parents=True, exist_ok=True)
    records = {}
    for character, runtime in [('feibi', ROOT / 'assets/characters/feibijiubi/runtime'),
                               ('nuonuo', ROOT / 'assets/sprites/runtime')]:
        clips = ['idle', 'walk', 'run', 'pair_cheek', 'pair_feed', 'pair_sleep', 'pair_notice', 'pair_nuzzle', 'pair_ball']
        if character == 'feibi': clips += ['hat_open', 'hat_wear', 'bare_tear']
        overview = Image.new('RGB', (8*192, len(clips)*210), '#eef2f5')
        draw = ImageDraw.Draw(overview)
        for row, clip in enumerate(clips):
            measures = []
            for i in range(16):
                frame = Image.open(runtime / clip / f'frame_{i:02d}.png').convert('RGBA')
                bounds = face_bounds(frame)
                measures.append({'frame': i, 'face': bounds,
                                 'silhouette': frame.getchannel('A').getbbox()})
                if i % 2 == 0:
                    overview.paste(frame.resize((192,192)), ((i//2)*192,row*210+18),
                                   frame.resize((192,192)))
            draw.text((4,row*210+2), clip, fill='black')
            records[f'{character}/{clip}'] = measures
            widths = [m['face'][2]-m['face'][0] for m in measures]
            centers = [(m['face'][2]+m['face'][0])/2 for m in measures]
            if clip in {'walk','run'}:
                assert max(centers)-min(centers) <= 1.5, f'{character}/{clip}: horizontal registration drift'
            if clip in {'pair_cheek','pair_feed','pair_sleep','pair_notice','pair_nuzzle','pair_ball'} and not (character == 'feibi' and clip == 'pair_feed'):
                idle = records[f'{character}/idle'][0]['face']
                assert abs(widths[0]/(idle[2]-idle[0])-1) < .10, f'{character}/{clip}: neutral body scale mismatch'
            print(f'{character}/{clip}: face width {min(widths)}..{max(widths)}, median {np.median(widths):.1f}; x {min(centers)}..{max(centers)}')
        overview.save(OUTPUT / f'{character}.png')
    (OUTPUT / 'measurements.json').write_text(json.dumps(records,indent=2),encoding='utf-8')

if __name__ == '__main__': main()
