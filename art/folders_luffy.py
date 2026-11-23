"""Glass folder icons in the Straw Hat palette: red glass back, warm gold frosted front."""
import os
import sys

import numpy as np
from PIL import Image, ImageChops, ImageDraw, ImageFilter, ImageFont

S = 1024
out_dir = sys.argv[1]
preview_bg = sys.argv[2]
os.makedirs(out_dir, exist_ok=True)
ICON_FONT = r"C:\Windows\Fonts\SegoeIcons.ttf"
GLYPHS = {"ai": "\uE99A", "dev": "\uE943", "creative": "\uE771", "productivity": "\uE73E",
          "system": "\uE713", "documents": "\uE8A5", "projects": "\uE8B7", "plain": ""}


def mask_rr(box, r):
    m = Image.new("L", (S, S), 0)
    ImageDraw.Draw(m).rounded_rectangle(box, r, fill=255)
    return m


def fill(mask, top, bot, y0, y1):
    yy = np.clip((np.arange(S) - y0) / max(1, y1 - y0), 0, 1)[:, None]
    arr = (np.array(top, float) * (1 - yy[..., None]) + np.array(bot, float) * yy[..., None]) * np.ones((S, S, 1))
    arr[..., 3] *= np.array(mask, float) / 255
    return Image.fromarray(arr.astype(np.uint8), "RGBA")


def rim(mask, width, color, blur, offset=(0, 0)):
    m = mask.filter(ImageFilter.GaussianBlur(width))
    edge = ImageChops.subtract(mask, ImageChops.offset(m, *offset)).filter(ImageFilter.GaussianBlur(blur))
    edge = ImageChops.multiply(edge, mask)
    layer = Image.new("RGBA", (S, S), color)
    layer.putalpha(ImageChops.multiply(edge, Image.new("L", (S, S), color[3])))
    return layer


def folder(glyph):
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    sh = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    ImageDraw.Draw(sh).rounded_rectangle((110, 300, 914, 890), 90, fill=(40, 10, 20, 120))
    img.alpha_composite(sh.filter(ImageFilter.GaussianBlur(36)), (0, 22))

    back = mask_rr((96, 160, 470, 340), 60)
    ImageDraw.Draw(back).rounded_rectangle((96, 225, 928, 870), 80, fill=255)
    back = back.filter(ImageFilter.GaussianBlur(1.5))
    img.alpha_composite(fill(back, (225, 55, 60, 150), (160, 22, 38, 170), 160, 870))
    img.alpha_composite(rim(back, 10, (255, 200, 190, 255), 2, (0, 4)))

    sheet = mask_rr((160, 270, 864, 560), 40)
    img.alpha_composite(fill(sheet, (255, 248, 235, 160), (255, 240, 220, 80), 270, 560))

    front = mask_rr((96, 345, 928, 870), 80).filter(ImageFilter.GaussianBlur(1.5))
    img.alpha_composite(fill(front, (255, 222, 130, 120), (240, 165, 60, 140), 345, 870))
    img.alpha_composite(rim(front, 26, (255, 238, 195, 255), 10))
    img.alpha_composite(rim(front, 5, (255, 255, 255, 255), 1.2, (3, 5)))
    img.alpha_composite(rim(front, 8, (255, 190, 120, 200), 3, (0, -8)))

    sheen = Image.new("L", (S, S), 0)
    ImageDraw.Draw(sheen).polygon([(96, 345), (560, 345), (300, 870), (96, 870)], fill=60)
    sheen = ImageChops.multiply(sheen.filter(ImageFilter.GaussianBlur(60)), front)
    sl = Image.new("RGBA", (S, S), (255, 255, 255, 0))
    sl.putalpha(sheen)
    img.alpha_composite(sl)

    # thin red band across the front, echoing the hat
    band = mask_rr((96, 410, 928, 452), 4)
    band = ImageChops.multiply(band, front)
    img.alpha_composite(fill(band, (214, 32, 44, 200), (190, 24, 36, 200), 410, 452))

    if glyph:
        f = ImageFont.truetype(ICON_FONT, 240)
        g = Image.new("RGBA", (S, S), (0, 0, 0, 0))
        d = ImageDraw.Draw(g)
        d.text((S // 2, 652), glyph, font=f, fill=(120, 40, 20, 110), anchor="mm")
        d.text((S // 2, 646), glyph, font=f, fill=(255, 255, 255, 240), anchor="mm")
        img.alpha_composite(g.filter(ImageFilter.GaussianBlur(12)))
        img.alpha_composite(g)
    return img


for name, glyph in GLYPHS.items():
    ico = folder(glyph)
    ico.save(os.path.join(out_dir, f"{name}.ico"), sizes=[(256, 256), (128, 128), (64, 64), (48, 48), (32, 32), (16, 16)])
    if name == "dev":
        bg = Image.open(preview_bg).crop((300, 1300, 1324, 2160)).resize((512, 430)).convert("RGBA")
        bg.alpha_composite(ico.resize((420, 420)), (46, 4))
        bg.save(os.path.join(out_dir, "preview.png"))
print("ok")
