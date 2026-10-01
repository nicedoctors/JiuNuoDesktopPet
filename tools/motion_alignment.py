"""Deterministic registration of locomotion cels; no generated model inputs."""
import numpy as np
import cv2
from PIL import Image


def face_bounds(frame):
    rgba = np.asarray(frame.convert('RGBA'))
    r, g, b = [rgba[:, :, i].astype(np.int16) for i in range(3)]
    mask = ((rgba[:, :, 3] > 220) & (r > 210) & (g > 170) & (b > 165)
            & (r-g > 3) & (r-g < 65) & (np.abs(g-b) < 22)).astype(np.uint8)
    mask[:round(frame.height*.15)] = 0
    mask[round(frame.height*.79):] = 0
    count, _, stats, _ = cv2.connectedComponentsWithStats(mask, 8)
    candidates = [i for i in range(1, count) if stats[i, cv2.CC_STAT_AREA] >= 40]
    if not candidates: raise ValueError('Face anchor not found')
    x,y,w,h,_ = stats[max(candidates, key=lambda i: stats[i, cv2.CC_STAT_AREA])]
    return int(x), int(y), int(x+w), int(y+h)


def align_locomotion(frames):
    bounds = [face_bounds(frame) for frame in frames]
    centers = [(b[0]+b[2])/2 for b in bounds]
    target = float(np.median(centers))
    output = []
    for frame, center in zip(frames, centers):
        dx = round(target-center)
        box = frame.getchannel('A').point(lambda a: 255 if a > 12 else 0).getbbox()
        if box[0]+dx < 2 or box[2]+dx > frame.width-2:
            raise ValueError('Locomotion registration would crop the silhouette')
        canvas = Image.new('RGBA', frame.size)
        canvas.alpha_composite(frame, (dx,0))
        output.append(canvas)
    return output


def foot_center(piece):
    alpha = np.asarray(piece.getchannel('A'))
    # The ground-contact band excludes raised feet, extended hands and flowing hair.
    xs = np.where(alpha[-max(4, round(piece.height*.035)):] > 128)[1]
    if not len(xs): raise ValueError('Foot anchor not found')
    return float((xs.min()+xs.max())/2)
