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

# ---- distant ship silhouette on the horizon (generic sailing ship, no emblem)
sx, sy = int(0.40 * W), HORIZON + 4
s = 1.0
hull = [(sx - 120 * s, sy - 18), (sx + 130 * s, sy - 18), (sx + 95 * s, sy + 8), (sx - 90 * s, sy + 8)]
silh = (20, 12, 30, 255)
draw.polygon(hull, fill=silh)
draw.polygon([(sx + 105, sy - 18), (sx + 160, sy - 30), (sx + 125, sy - 18)], fill=silh)
for mx, mh in ((sx - 40, 170), (sx + 40, 140)):
    draw.rectangle([mx - 3, sy - 18 - mh, mx + 3, sy - 18], fill=silh)
    draw.polygon([(mx - 55, sy - 30 - mh * 0.85), (mx + 55, sy - 30 - mh * 0.85),
                  (mx + 62, sy - 40 - mh * 0.25), (mx - 62, sy - 40 - mh * 0.25)], fill=(32, 18, 40, 255))
    draw.rectangle([mx - 2, sy - 26 - mh, mx + 12, sy - 14 - mh], fill=(150, 25, 35, 255))

# ---- seagulls
for gx, gy, gs in ((0.52, 0.22, 1.0), (0.56, 0.18, 0.7), (0.47, 0.26, 0.55), (0.83, 0.28, 0.8)):
    cx, cy, k = gx * W, gy * H, 26 * gs
    draw.line([(cx - k, cy), (cx - k * 0.4, cy - k * 0.35), (cx, cy)], fill=(25, 15, 35, 255), width=int(5 * gs) + 1, joint="curve")
    draw.line([(cx, cy), (cx + k * 0.4, cy - k * 0.35), (cx + k, cy)], fill=(25, 15, 35, 255), width=int(5 * gs) + 1, joint="curve")


# ---- the straw hat, drawn on its own layer, then tilted and placed in the wind
def straw_hat(size=1500):
    hat = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    hy, hx = np.mgrid[0:size, 0:size].astype(np.float32)
    cx, cy = size / 2, size * 0.58
    # brim: wide ellipse
    bw, bh = size * 0.47, size * 0.17
    e = ((hx - cx) / bw) ** 2 + ((hy - cy) / bh) ** 2
    brim = np.clip((1 - e) * 60, 0, 1)
    # crown: dome above the brim
    cw, ch = size * 0.2, size * 0.2
    crown_cy = cy - size * 0.11
    ec = ((hx - cx) / cw) ** 2 + ((hy - crown_cy) / ch) ** 2
    crown = np.clip((1 - ec) * 60, 0, 1) * (hy < cy + size * 0.02)
    # radial weave rings + strand noise
    rr = np.sqrt(((hx - cx) / bw) ** 2 + ((hy - cy) / bh) ** 2)
    weave = 0.5 + 0.5 * np.sin(rr * 140)
    strands = rng.normal(0, 1, (size, size)).astype(np.float32)
    strands = np.array(Image.fromarray(((strands * 30) + 128).clip(0, 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(1.2)), np.float32) / 255 - 0.5
    straw_light, straw_dark = col("f2cf6e"), col("b8862b")
    # lighting: warm from the sun side (right), shade under the crown
    light = np.clip(0.55 + (hx - cx) / size * 0.9 - (hy - cy) / size * 0.4, 0, 1)
    brim_col = straw_dark + (straw_light - straw_dark) * (light * 0.8 + weave * 0.2)[..., None]
    brim_col += (strands * 40)[..., None]
    crown_rr = np.sqrt(((hx - cx) / cw) ** 2 + ((hy - crown_cy) / ch) ** 2)
    crown_weave = 0.5 + 0.5 * np.sin(crown_rr * 70 + (hy - crown_cy) / 9)
    crown_light = np.clip(0.45 + (hx - cx) / cw * 0.35 - (hy - crown_cy) / ch * 0.35, 0, 1)
    crown_col = straw_dark + (straw_light - straw_dark) * (crown_light * 0.8 + crown_weave * 0.2)[..., None]
    crown_col += (strands * 35)[..., None]
    # red band around the crown base
    band_top, band_bot = cy - size * 0.055, cy + size * 0.03
    band = crown * (hy > band_top) * (hy < band_bot)
    red = col("c8202c") * (0.75 + 0.45 * crown_light)[..., None]
    # shadow of the crown on the brim
    shadow = np.exp(-(((hx - cx - size * 0.03) / (cw * 1.3)) ** 2 + ((hy - cy - size * 0.02) / (bh * 0.55)) ** 2))
    brim_col *= (1 - 0.35 * shadow)[..., None]
    rgb = brim_col * brim[..., None]
    rgb = rgb * (1 - crown[..., None]) + crown_col * crown[..., None]
    rgb = rgb * (1 - band[..., None]) + red * band[..., None]
    # rim light on the sun side
    rim = np.clip((e - 0.86) * 8, 0, 1) * brim * np.clip((hx - cx) / bw, 0, 1)
    rgb += (col("fff0b0") * 0.6)[None, None, :] * rim[..., None]
    alpha = np.clip(np.maximum(brim, crown), 0, 1)
    arr = np.dstack([np.clip(rgb, 0, 255), alpha * 255]).astype(np.uint8)
    return Image.fromarray(arr, "RGBA")


hat = straw_hat().rotate(-14, resample=Image.BICUBIC, expand=True)
hat = hat.resize((int(hat.width * 0.62), int(hat.height * 0.62)), Image.LANCZOS)
hx0, hy0 = int(0.70 * W - hat.width / 2), int(0.30 * H - hat.height / 2)
# soft shadow cast on the sky haze for depth
sh = Image.new("RGBA", hat.size, (0, 0, 0, 0))
sh.putalpha(hat.getchannel("A").point(lambda a: int(a * 0.35)))
sh = sh.filter(ImageFilter.GaussianBlur(30))
base.alpha_composite(sh, (hx0 + 40, hy0 + 60))
base.alpha_composite(hat, (hx0, hy0))

# wind streaks trailing behind the hat
wind = Image.new("RGBA", (W, H), (0, 0, 0, 0))
wd = ImageDraw.Draw(wind)
for i in range(7):
    yy = hy0 + hat.height * (0.35 + i * 0.06)
    x1 = hx0 - 200 - i * 60
    wd.line([(x1 - 700, yy + 60), (x1 - 300, yy + 20), (x1, yy)], fill=(255, 230, 200, 55), width=4)
base.alpha_composite(wind.filter(ImageFilter.GaussianBlur(3)))

final = base.convert("RGB")
noise = (rng.normal(0, 2.0, (H, W, 3))).astype(np.float32)
final = Image.fromarray(np.clip(np.array(final, np.float32) + noise, 0, 255).astype(np.uint8))
final.save(out, quality=95)
final.resize((1280, 720), Image.LANCZOS).save(out.replace(".jpg", "-preview.jpg"), quality=90)
print("saved", out)
