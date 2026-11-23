import os
"""Original 'Straw Hat Sunset' wallpaper: a straw hat with a red band blown over a sunset sea."""
import math
import sys

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

W, H = 3840, 2160
HORIZON = int(H * 0.60)
out = sys.argv[1]
rng = np.random.default_rng(42)


def col(h):
    return np.array([int(h[i:i + 2], 16) for i in (0, 2, 4)], np.float32)


def ramp(stops, t):
    """Piecewise-linear colour ramp; stops = [(pos, hex), ...]."""
    t = np.clip(t, 0, 1)
    out_ = np.zeros(t.shape + (3,), np.float32)
    for (p0, c0), (p1, c1) in zip(stops, stops[1:]):
        m = (t >= p0) & (t <= p1)
        f = ((t - p0) / max(p1 - p0, 1e-6))[..., None]
        out_[m] = (col(c0) * (1 - f) + col(c1) * f)[m]
    return out_


y, x = np.mgrid[0:H, 0:W].astype(np.float32)
u, v = x / W, y / H

# ---- sky: night blue -> violet -> rose -> orange -> gold at the horizon
sky_t = v / (HORIZON / H)
img = ramp([(0, "0a0f2e"), (0.35, "2b1d5c"), (0.62, "8a2f6b"), (0.82, "e0573f"), (0.95, "f7a03a"), (1.0, "ffd36b")], sky_t)

SUN_X, SUN_Y, SUN_R = 0.64 * W, HORIZON - 40, 230
d_sun = np.sqrt((x - SUN_X) ** 2 + (y - SUN_Y) ** 2)
glow = np.exp(-(d_sun / 900) ** 2)[..., None]
img = img * (1 - 0.55 * glow) + col("ffb347") * 0.55 * glow
halo = np.exp(-(d_sun / 380) ** 2)[..., None]
img = img * (1 - 0.6 * halo) + col("ffe08a") * 0.6 * halo
disc = np.clip((SUN_R - d_sun) / 3, 0, 1)[..., None] * (y < HORIZON)[..., None]
img = img * (1 - disc) + col("fff1c4") * disc

# ---- clouds: soft horizontal streaks lit from below
cloud = np.zeros((H, W), np.float32)
for i in range(14):
    cy = rng.uniform(0.12, 0.5) * H
    cx = rng.uniform(-0.1, 1.1) * W
    length = rng.uniform(0.25, 0.6) * W
    thick = rng.uniform(18, 55)
    cloud += np.exp(-((y - cy) / thick) ** 2 - ((x - cx) / length) ** 2) * rng.uniform(0.35, 0.8)
cloud = np.clip(cloud, 0, 1)[..., None]
lit = ramp([(0, "3a2466"), (0.5, "c0507a"), (1, "ff9a5c")], np.clip(v / (HORIZON / H), 0, 1))
img = img * (1 - 0.55 * cloud) + lit * 0.55 * cloud

# ---- sea: deep blue, brighter near the horizon, golden sun path
sea = y >= HORIZON
sea_t = (y - HORIZON) / (H - HORIZON)
sea_col = ramp([(0, "3b3f8f"), (0.15, "1f3a8a"), (0.6, "112a66"), (1, "081a40")], sea_t)
img[sea] = sea_col[sea]

# wave texture: layered sines compressed toward the horizon (perspective)
depth = np.clip(sea_t, 1e-3, 1)
rows = H - HORIZON


def streaks(cell_w, cell_h, seed):
    """Random noise stretched horizontally: reads as wave crests, no regular pattern."""
    g = np.random.default_rng(seed).normal(0, 1, (max(2, rows // cell_h), max(2, W // cell_w))).astype(np.float32)
    im = Image.fromarray(((g * 40) + 128).clip(0, 255).astype(np.uint8)).resize((W, rows), Image.BICUBIC)
    return np.array(im, np.float32) / 128 - 1


wave_rows = 0.6 * streaks(90, 3, 1) + 0.4 * streaks(30, 2, 2)
wave = np.zeros((H, W), np.float32)
wave[HORIZON:] = wave_rows
img[sea] += (wave[sea] * (6 + 22 * depth[sea]))[:, None]

# sun reflection: a broken golden column under the sun
path = np.exp(-((x - SUN_X) / (60 + 520 * depth)) ** 2)
sparkle = np.clip(wave * 1.6 + 0.2, 0, 1)
refl = (path * sparkle * (1 - depth * 0.6))[..., None]
img = np.where(sea[..., None], img * (1 - 0.8 * refl) + col("ffd27a") * 0.8 * refl, img)
hz = np.exp(-((y - HORIZON) / 6) ** 2)[..., None]
img = img * (1 - 0.5 * hz) + col("ffc67a") * 0.5 * hz

# keep the icon side (left) calmer for readable labels
left = np.clip(1 - u / 0.28, 0, 1)[..., None] ** 1.5
img *= 1 - 0.28 * left

# vignette
r = np.sqrt((u - 0.55) ** 2 * 1.2 + (v - 0.5) ** 2)
img *= (1 - 0.45 * np.clip(r - 0.35, 0, 1))[..., None]

base = Image.fromarray(np.clip(img, 0, 255).astype(np.uint8)).convert("RGBA")
draw = ImageDraw.Draw(base)


# ================= scene split: clean wallpaper + moving pieces =================
import json

scene_dir = sys.argv[2]
os.makedirs(scene_dir, exist_ok=True)
HALF = 0.5  # pieces are saved at screen size (3840 -> 1920 wide)

# ---- ship on its own layer
ship_layer = Image.new("RGBA", (W, H), (0, 0, 0, 0))
sd = ImageDraw.Draw(ship_layer)
sx, sy = int(0.40 * W), HORIZON + 4
silh = (20, 12, 30, 255)
sd.polygon([(sx - 120, sy - 18), (sx + 130, sy - 18), (sx + 95, sy + 8), (sx - 90, sy + 8)], fill=silh)
sd.polygon([(sx + 105, sy - 18), (sx + 160, sy - 30), (sx + 125, sy - 18)], fill=silh)
for mx, mh in ((sx - 40, 170), (sx + 40, 140)):
    sd.rectangle([mx - 3, sy - 18 - mh, mx + 3, sy - 18], fill=silh)
    sd.polygon([(mx - 55, sy - 30 - mh * 0.85), (mx + 55, sy - 30 - mh * 0.85),
                (mx + 62, sy - 40 - mh * 0.25), (mx - 62, sy - 40 - mh * 0.25)], fill=(32, 18, 40, 255))
    sd.rectangle([mx - 2, sy - 26 - mh, mx + 12, sy - 14 - mh], fill=(150, 25, 35, 255))
box = ship_layer.getbbox()
ship = ship_layer.crop(box)
ship = ship.resize((int(ship.width * HALF), int(ship.height * HALF)), Image.LANCZOS)
ship.save(os.path.join(scene_dir, "ship.png"))
ship_waterline = (sy + 8 - box[1]) * HALF  # y of the hull bottom inside ship.png


# ---- straw hat on its own layer (with its soft shadow)
exec(open(sys.argv[3]).read())  # defines straw_hat() from the original wallpaper script
hat = straw_hat().rotate(-14, resample=Image.BICUBIC, expand=True)
hat = hat.resize((int(hat.width * 0.62), int(hat.height * 0.62)), Image.LANCZOS)
margin = 120
canvas = Image.new("RGBA", (hat.width + 2 * margin, hat.height + 2 * margin), (0, 0, 0, 0))
sh = Image.new("RGBA", hat.size, (0, 0, 0, 0))
sh.putalpha(hat.getchannel("A").point(lambda a: int(a * 0.35)))
canvas.alpha_composite(sh.filter(ImageFilter.GaussianBlur(30)), (margin + 40, margin + 60))
canvas.alpha_composite(hat, (margin, margin))
canvas = canvas.resize((int(canvas.width * HALF), int(canvas.height * HALF)), Image.LANCZOS)
canvas.save(os.path.join(scene_dir, "hat.png"))

final = base.convert("RGB")
noise = (rng.normal(0, 2.0, (H, W, 3))).astype(np.float32)
final = Image.fromarray(np.clip(np.array(final, np.float32) + noise, 0, 255).astype(np.uint8))
final.save(out, quality=95)
final.resize((1280, 720), Image.LANCZOS).save(out.replace(".jpg", "-preview.jpg"), quality=90)

info = {
    "width": W * HALF, "height": H * HALF, "horizon": HORIZON * HALF,
    "hatCenter": [0.70 * W * HALF, 0.30 * H * HALF],
    "shipX": sx * HALF, "shipWaterline": ship_waterline,
    "sunX": SUN_X * HALF, "sunR": SUN_R * HALF,
}
json.dump(info, open(os.path.join(scene_dir, "scene.json"), "w"), indent=1)
print("saved", out, info)
