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


