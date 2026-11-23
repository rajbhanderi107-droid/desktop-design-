"""macOS-style folder icons, one colour per folder from the Straw Hat palette, with a debossed symbol."""
import colorsys
import os
import sys

import numpy as np
from PIL import Image, ImageChops, ImageDraw, ImageFilter, ImageFont

S = 1024
out_dir = sys.argv[1]
ICON_FONT = r"C:\Windows\Fonts\SegoeIcons.ttf"

# name: (base colour, glyph)
FOLDERS = {
    "ai":           ("#E0393E", "\uE99A"),  # straw-hat red, robot
    "dev":          ("#B5202D", "\uE943"),  # deep crimson, code
    "creative":     ("#E8577E", "\uE771"),  # coral pink, brush
    "productivity": ("#F2B233", "\uE73E"),  # straw gold, check
    "system":       ("#8E5BD9", "\uE7FC"),  # violet, game controller
    "documents":    ("#F27A2E", "\uE8A5"),  # sunset orange, document
    "apps":         ("#1FA9A3", "\uE71D"),  # lagoon teal, app grid
    "projects":     ("#3FA2F2", "\uE821"),  # sky blue, briefcase
    "plain":        ("#C9A46A", ""),        # sand
}


def hex_rgb(h):
    h = h.lstrip("#")
    return tuple(int(h[i:i + 2], 16) for i in (0, 2, 4))


def shade(rgb, light=0.0, sat=0.0):
    r, g, b = [c / 255 for c in rgb]
    hh, ll, ss = colorsys.rgb_to_hls(r, g, b)
    ll = max(0, min(1, ll + light))
    ss = max(0, min(1, ss + sat))
    return tuple(int(c * 255) for c in colorsys.hls_to_rgb(hh, ll, ss))


def vgrad(box, top, bot, alpha=255):
    x0, y0, x1, y1 = box
    h = y1 - y0
    t = np.linspace(0, 1, h)[:, None, None]
    col = (np.array(top, float) * (1 - t) + np.array(bot, float) * t)
    arr = np.ones((h, x1 - x0, 1)) * col
    a = np.full((h, x1 - x0, 1), alpha, float)
    return Image.fromarray(np.concatenate([arr, a], 2).astype(np.uint8), "RGBA")


def layer_from_mask(mask, fill_img, at):
    layer = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    layer.paste(fill_img, at)
    layer.putalpha(ImageChops.multiply(layer.getchannel("A"), mask))
    return layer


def folder(base_hex, glyph):
    base = hex_rgb(base_hex)
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))

    # soft contact shadow
    sh = Image.new("L", (S, S), 0)
    ImageDraw.Draw(sh).rounded_rectangle((120, 300, 904, 880), 70, fill=110)
    sh = sh.filter(ImageFilter.GaussianBlur(34))
    shadow = Image.new("RGBA", (S, S), (10, 8, 20, 0))
    shadow.putalpha(sh)
    img.alpha_composite(shadow, (0, 26))

    # back flap with tab
    back = Image.new("L", (S, S), 0)
    d = ImageDraw.Draw(back)
    d.rounded_rectangle((104, 196, 440, 330), 46, fill=255)
    d.polygon([(360, 196), (440, 196), (500, 262), (500, 330), (360, 330)], fill=255)
    d.rounded_rectangle((104, 248, 920, 840), 58, fill=255)
    back = back.filter(ImageFilter.GaussianBlur(1.2))
    img.alpha_composite(layer_from_mask(back, vgrad((0, 0, S, S), shade(base, -0.06), shade(base, -0.16)), (0, 0)))

    # paper sheet peeking out
    paper = Image.new("L", (S, S), 0)
    ImageDraw.Draw(paper).rounded_rectangle((150, 290, 874, 560), 26, fill=255)
    img.alpha_composite(layer_from_mask(paper, vgrad((0, 0, S, S), (255, 255, 255), (232, 234, 240)), (0, 0)))

    # front panel: lighter at the top, gentle curve on the top edge
    front = Image.new("L", (S, S), 0)
    fd = ImageDraw.Draw(front)
    fd.rounded_rectangle((104, 352, 920, 840), 58, fill=255)
    front = front.filter(ImageFilter.GaussianBlur(1.2))
    img.alpha_composite(layer_from_mask(front, vgrad((0, 0, S, S), shade(base, 0.14, 0.05), shade(base, -0.02)), (0, 0)))

    # top highlight line and faint lower edge
    hl = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    hd = ImageDraw.Draw(hl)
    hd.rounded_rectangle((106, 354, 918, 838), 56, outline=(255, 255, 255, 90), width=4)
    hl = hl.filter(ImageFilter.GaussianBlur(1))
    top_only = Image.new("L", (S, S), 0)
    ImageDraw.Draw(top_only).rectangle((0, 0, S, 470), fill=255)
    hl.putalpha(ImageChops.multiply(hl.getchannel("A"), top_only.filter(ImageFilter.GaussianBlur(40))))
    img.alpha_composite(hl)

    # debossed symbol: darker shape with a light lip below, as on macOS folders
    if glyph:
        f = ImageFont.truetype(ICON_FONT, 250)
        cx, cy = S // 2, 610
        dark = Image.new("RGBA", (S, S), (0, 0, 0, 0))
        ImageDraw.Draw(dark).text((cx, cy), glyph, font=f, fill=shade(base, -0.22) + (210,), anchor="mm")
        lip = Image.new("RGBA", (S, S), (0, 0, 0, 0))
        ImageDraw.Draw(lip).text((cx, cy + 5), glyph, font=f, fill=shade(base, 0.25) + (150,), anchor="mm")
        img.alpha_composite(lip.filter(ImageFilter.GaussianBlur(1.5)))
        img.alpha_composite(dark)
    return img


os.makedirs(out_dir, exist_ok=True)
sheet = Image.new("RGBA", (len(FOLDERS) * 180, 180), (24, 20, 40, 255))
for i, (name, (col, glyph)) in enumerate(FOLDERS.items()):
    im = folder(col, glyph)
    im.save(os.path.join(out_dir, name + ".ico"), sizes=[(256, 256), (128, 128), (64, 64), (48, 48), (32, 32), (16, 16)])
    im.resize((128, 128), Image.LANCZOS).save(os.path.join(out_dir, name + ".png"))
    sheet.alpha_composite(im.resize((160, 160), Image.LANCZOS), (i * 180 + 10, 10))
sheet.save(os.path.join(out_dir, "preview.png"))
print("ok")
