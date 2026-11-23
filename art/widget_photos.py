"""Cuts five square photos for the photo widgets out of the Straw Hat Sunset wallpaper."""
import os
import sys

from PIL import Image, ImageEnhance

src, out_dir = sys.argv[1], sys.argv[2]
os.makedirs(out_dir, exist_ok=True)
im = Image.open(src).convert("RGB")
W, H = im.size

crops = {
    "photo1.jpg": (0.575, 0.05, 0.46),   # the straw hat
    "photo2.jpg": (0.335, 0.47, 0.20),   # ship on the horizon
    "photo3.jpg": (0.565, 0.46, 0.30),    # sun touching the sea
    "photo4.jpg": (0.44, 0.10, 0.22),    # seagulls in the dusk sky
    "photo5.jpg": (0.55, 0.62, 0.30),    # golden path on the water
}
for name, (fx, fy, fs) in crops.items():
    side = int(fs * H)
    x0, y0 = int(fx * W), int(fy * H)
    tile = im.crop((x0, y0, x0 + side, y0 + side)).resize((600, 600), Image.LANCZOS)
    tile = ImageEnhance.Contrast(tile).enhance(1.06)
    tile.save(os.path.join(out_dir, name), quality=92)
print("ok")
