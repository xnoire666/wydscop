"""Draws the Wydscop logo and writes the app/tray icon and the logo images.

Run from the repo root:  python tools/make-icon.py
Needs Pillow (pip install pillow). Writes:
  src/Wydscop/Assets/Wydscop.ico   app + tray icon (16..256 px)
  assets/logo.png                  512 px logo for the README
  assets/logo.svg                  the same drawing as a vector

The logo is a "WS" monogram in glowing aurora colors (sky blue, indigo, fuchsia, coral) on a
dark tile, with a small sparkle. The letters are drawn as thick rounded strokes, not set in a
font, so the logo needs no font license. Tray sizes (24 px and below) fill the whole square,
use bolder, brighter letters and drop the glow and sparkle so they stay crisp at 16 px.
"""
from PIL import Image, ImageDraw, ImageFilter
import math, os

SS = 1024  # every size is drawn at this resolution, then scaled down

AURORA = [(0.0, (56, 189, 248)), (0.35, (99, 102, 241)), (0.7, (217, 70, 239)), (1.0, (251, 113, 133))]
# Tiny icons sit on a dark tile on a (maybe) dark taskbar: lighter colors keep the letters readable.
AURORA_BRIGHT = [(0.0, (125, 211, 252)), (0.35, (165, 160, 255)), (0.7, (240, 130, 250)), (1.0, (253, 164, 175))]
TILE_TOP, TILE_BOTTOM = (34, 30, 58), (12, 11, 22)


def lerp(a, b, t):
    return tuple(x + (y - x) * t for x, y in zip(a, b))


def linear(n, stops, angle=35):
    """Linear gradient, rotated `angle` degrees clockwise from left-to-right."""
    strip = Image.new("RGB", (512, 1))
    for i in range(512):
        t = i / 511
        for (t0, c0), (t1, c1) in zip(stops, stops[1:]):
            if t0 <= t <= t1:
                strip.putpixel((i, 0), tuple(round(v) for v in lerp(c0, c1, (t - t0) / (t1 - t0))))
                break
    big = strip.resize((n * 2, n * 2)).rotate(-angle, Image.BICUBIC)
    o = n // 2
    return big.crop((o, o, o + n, o + n))


def vertical(n, top, bottom):
    g = Image.new("RGB", (1, 256))
    for y in range(256):
        g.putpixel((0, y), tuple(round(v) for v in lerp(top, bottom, y / 255)))
    return g.resize((n, n))


def dot(d, x, y, w):
    d.ellipse([x - w / 2, y - w / 2, x + w / 2, y + w / 2], fill=255)


def letters(n, small):
    """The W and the S as one white-on-black stroke mask."""
    m = Image.new("L", (n, n), 0)
    d = ImageDraw.Draw(m)
    w = n * (0.105 if small else 0.08)
    top, bot = (0.30, 0.70) if small else (0.35, 0.67)
    x0, x1 = (0.10, 0.56) if small else (0.15, 0.53)
    h = bot - top
    # W: four strokes; the middle peak sits lower than the two tops
    xs = [x0 + (x1 - x0) * k / 4 for k in range(5)]
    ys = [top, bot, top + h * 0.28, bot, top]
    pts = [(x * n, y * n) for x, y in zip(xs, ys)]
    d.line(pts, fill=255, width=round(w), joint="curve")
    for p in pts:
        dot(d, p[0], p[1], w)
    # S: two stacked bowls. Stamped as dots along the curve for smooth edges and round ends.
    r = h / 4 * n
    sx = (0.75 if small else 0.725) * n
    cy1, cy2 = top * n + r, top * n + 3 * r
    path = [(sx + r * 1.05 * math.cos(math.radians(-25 - k * 245 / 120)),
             cy1 + r * math.sin(math.radians(-25 - k * 245 / 120))) for k in range(121)]
    path += [(sx + r * 1.05 * math.cos(math.radians(-90 + k * 245 / 120)),
              cy2 + r * math.sin(math.radians(-90 + k * 245 / 120))) for k in range(121)]
    for (ax, ay), (bx, by) in zip(path, path[1:]):
        steps = max(1, int(math.hypot(bx - ax, by - ay) / 2))
        for t in range(steps + 1):
            dot(d, ax + (bx - ax) * t / steps, ay + (by - ay) * t / steps, w)
    return m


def sparkle(img, n, sx, sy, sr):
    k = sr * 0.22
    m = Image.new("L", (n, n), 0)
    ImageDraw.Draw(m).polygon([(sx, sy - sr), (sx + k, sy - k), (sx + sr, sy), (sx + k, sy + k),
                               (sx, sy + sr), (sx - k, sy + k), (sx - sr, sy), (sx - k, sy - k)], fill=255)
    img.paste((255, 255, 255, 255), (0, 0), m)


def draw(size):
    n = SS
    small = size <= 24
    out = vertical(n, TILE_TOP, TILE_BOTTOM).convert("RGBA")
    mask = letters(n, small)
    colors = linear(n, AURORA_BRIGHT if size <= 32 else AURORA).convert("RGBA")
    if not small:
        glow = colors.copy()
        glow.putalpha(mask.filter(ImageFilter.GaussianBlur(n * 0.04)).point(lambda v: v * 0.8))
        out = Image.alpha_composite(out, glow)
    out.paste(colors, (0, 0), mask)
    if not small:
        sparkle(out, n, n * 0.835, n * 0.20, n * 0.055)
    pad = 0 if small else n * 0.04
    tile = Image.new("L", (n, n), 0)
    ImageDraw.Draw(tile).rounded_rectangle([pad, pad, n - 1 - pad, n - 1 - pad], n * 0.23, fill=255)
    final = Image.new("RGBA", (n, n), (0, 0, 0, 0))
    final.paste(out, (0, 0), tile)
    return final.resize((size, size), Image.LANCZOS)


# The same drawing as a vector (512 x 512), from the numbers in letters() and draw().
SVG = """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 512 512" width="512" height="512">
  <defs>
    <linearGradient id="tile" x1="0" y1="0" x2="0" y2="1">
      <stop offset="0" stop-color="#221E3A"/>
      <stop offset="1" stop-color="#0C0B16"/>
    </linearGradient>
    <linearGradient id="aurora" gradientUnits="userSpaceOnUse" x1="-163.4" y1="-37.7" x2="675.4" y2="549.7">
      <stop offset="0" stop-color="#38BDF8"/>
      <stop offset="0.35" stop-color="#6366F1"/>
      <stop offset="0.7" stop-color="#D946EF"/>
      <stop offset="1" stop-color="#FB7185"/>
    </linearGradient>
    <filter id="glow" filterUnits="userSpaceOnUse" x="0" y="0" width="512" height="512">
      <feGaussianBlur stdDeviation="20"/>
    </filter>
    <clipPath id="clip"><rect x="20.5" y="20.5" width="471" height="471" rx="117.8"/></clipPath>
  </defs>
  <g clip-path="url(#clip)">
    <rect width="512" height="512" fill="url(#tile)"/>
    <g id="ws" fill="none" stroke="url(#aurora)" stroke-width="41" stroke-linecap="round" stroke-linejoin="round">
      <path d="M76.8 179.2 L125.4 343 L174.1 225.1 L222.7 343 L271.4 179.2"/>
      <path d="M410.2 202.9 A43 41 0 1 0 371.2 261.1 A43 41 0 1 1 332.2 319.4"/>
    </g>
    <use href="#ws" filter="url(#glow)" opacity="0.8"/>
    <use href="#ws"/>
    <path d="M427.5 74.2 L433.7 96.2 L455.7 102.4 L433.7 108.6 L427.5 130.6 L421.3 108.6 L399.3 102.4 L421.3 96.2 Z" fill="#fff"/>
  </g>
</svg>
"""

if __name__ == "__main__":
    root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256]
    imgs = [draw(s) for s in sizes]
    ico = os.path.join(root, "src", "Wydscop", "Assets", "Wydscop.ico")
    imgs[-1].save(ico, sizes=[(s, s) for s in sizes], append_images=imgs[:-1])
    draw(512).save(os.path.join(root, "assets", "logo.png"))
    with open(os.path.join(root, "assets", "logo.svg"), "w", encoding="utf-8") as f:
        f.write(SVG)
    print("wrote", ico)
