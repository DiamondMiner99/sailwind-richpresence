"""Draws the Discord Rich Presence images into art/. Run from the repo root: python tools/make_art.py

Each image is drawn at 4x and scaled down for smooth edges. File names are the Art Asset keys the mod uses.
"""
import math
import os

from PIL import Image, ImageDraw

SS = 4
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "art")

NAVY = (27, 40, 56)
CREAM = (240, 226, 196)
TAN = (214, 160, 94)
DARK = (16, 22, 32)


def lerp(a, b, t):
    return tuple(int(round(a[i] + (b[i] - a[i]) * t)) for i in range(3))


def canvas(size, color):
    return Image.new("RGB", (size * SS, size * SS), color)


def save(img, size, name):
    img.resize((size, size), Image.LANCZOS).save(os.path.join(OUT, name + ".png"))


def s(*values):
    return [v * SS for v in values]


def bezier(points, steps=200):
    """Samples a quadratic or cubic Bezier given as 3 or 4 control points."""
    out = []
    for i in range(steps + 1):
        t = i / steps
        if len(points) == 3:
            (x0, y0), (x1, y1), (x2, y2) = points
            x = (1 - t) ** 2 * x0 + 2 * (1 - t) * t * x1 + t ** 2 * x2
            y = (1 - t) ** 2 * y0 + 2 * (1 - t) * t * y1 + t ** 2 * y2
        else:
            (x0, y0), (x1, y1), (x2, y2), (x3, y3) = points
            x = (1 - t) ** 3 * x0 + 3 * (1 - t) ** 2 * t * x1 + 3 * (1 - t) * t ** 2 * x2 + t ** 3 * x3
            y = (1 - t) ** 3 * y0 + 3 * (1 - t) ** 2 * t * y1 + 3 * (1 - t) * t ** 2 * y2 + t ** 3 * y3
        out.append((x, y))
    return out


def stroke(d, path, width, color, end_width=None):
    """Round-capped line along a sampled path, drawn as overlapping discs so joints stay smooth.
    Tapers from width to end_width when given."""
    end_width = width if end_width is None else end_width
    total = sum(math.hypot(path[i + 1][0] - path[i][0], path[i + 1][1] - path[i][1]) for i in range(len(path) - 1))
    walked = 0.0
    for i in range(len(path) - 1):
        (x0, y0), (x1, y1) = path[i], path[i + 1]
        seg = math.hypot(x1 - x0, y1 - y0)
        n = max(1, int(seg / 0.75))
        for k in range(n):
            t = k / n
            r = (width + (end_width - width) * ((walked + seg * t) / total if total else 0)) / 2 * SS
            x, y = (x0 + (x1 - x0) * t) * SS, (y0 + (y1 - y0) * t) * SS
            d.ellipse([x - r, y - r, x + r, y + r], fill=color)
        walked += seg
    x, y = path[-1]
    r = end_width / 2 * SS
    d.ellipse([x * SS - r, y * SS - r, x * SS + r, y * SS + r], fill=color)


def large():
    size = 1024
    img = canvas(size, NAVY)
    d = ImageDraw.Draw(img)
    horizon = 640

    sky = [(0.0, (30, 38, 66)), (0.55, (122, 72, 92)), (0.85, (214, 122, 82)), (1.0, (243, 180, 110))]
    for y in range(horizon * SS):
        t = y / (horizon * SS)
        for i in range(len(sky) - 1):
            if sky[i][0] <= t <= sky[i + 1][0]:
                c = lerp(sky[i][1], sky[i + 1][1], (t - sky[i][0]) / (sky[i + 1][0] - sky[i][0]))
                break
        d.line([(0, y), (size * SS, y)], fill=c)

    d.ellipse(s(640 - 120, horizon - 120, 640 + 120, horizon + 120), fill=(255, 214, 150))

    for y in range(horizon * SS, size * SS):
        t = (y - horizon * SS) / ((size - horizon) * SS)
        d.line([(0, y), (size * SS, y)], fill=lerp((58, 70, 96), (14, 26, 40), t ** 0.7))

    # Sun glitter on the water.
    for i, (y, half) in enumerate([(662, 120), (690, 95), (722, 70), (760, 50), (806, 34), (860, 22)]):
        d.rounded_rectangle(s(640 - half, y, 640 + half, y + 7 - i // 2), radius=3 * SS, fill=(240, 170, 104))

    # Dhow: hull, lateen sail on a long yard, mast.
    hull = [(150, 664), (230, 700), (300, 738), (600, 740), (690, 700), (742, 640), (712, 646), (640, 676), (420, 690), (240, 682)]
    tack, peak, clew = (236, 668), (760, 214), (650, 670)
    sail = [tack] + bezier([peak, (748, 470), clew], 60)
    d.polygon([p * SS for xy in sail for p in xy], fill=CREAM)
    d.line(s(486, 690, 468, 440), fill=DARK, width=14 * SS)
    d.line(s(206, 694, 772, 204), fill=DARK, width=12 * SS)
    d.polygon([p * SS for xy in hull for p in xy], fill=DARK)

    save(img, size, "sailwind")


def badge():
    size = 512
    return canvas(size, NAVY), size


def anchored():
    img, size = badge()
    d = ImageDraw.Draw(img)
    d.ellipse(s(256 - 40, 96 - 40, 256 + 40, 96 + 40), outline=CREAM, width=20 * SS)
    d.rounded_rectangle(s(242, 130, 270, 400), radius=8 * SS, fill=CREAM)
    d.rounded_rectangle(s(172, 150, 340, 178), radius=12 * SS, fill=CREAM)
    d.arc(s(256 - 140, 272 - 140, 256 + 140, 272 + 140), start=20, end=160, fill=CREAM, width=28 * SS)
    for angle, sign in ((160, 1), (20, -1)):
        a = math.radians(angle)
        x, y = 256 + 140 * math.cos(a), 272 + 140 * math.sin(a)
        dx, dy = -math.sin(a) * sign, math.cos(a) * sign
        nx, ny = math.cos(a), math.sin(a)
        tip = (x + dx * 58, y + dy * 58)
        base = (x - dx * 6, y - dy * 6)
        head = [(base[0] + nx * 34, base[1] + ny * 34), tip, (base[0] - nx * 34, base[1] - ny * 34)]
        d.polygon([p * SS for xy in head for p in xy], fill=CREAM)
    d.ellipse(s(256 - 22, 400 - 22, 256 + 22, 400 + 22), fill=CREAM)
    save(img, size, "anchored")


def moored():
    img, size = badge()
    d = ImageDraw.Draw(img)
    # Rope running in from the water to the post, then turned round it.
    stroke(d, bezier([(70, 130), (150, 196), (214, 250)], 60), 26, TAN)
    d.rounded_rectangle(s(166, 392, 346, 424), radius=10 * SS, fill=CREAM)
    d.rounded_rectangle(s(206, 200, 306, 400), radius=14 * SS, fill=CREAM)
    d.ellipse(s(180, 150, 332, 226), fill=CREAM)
    d.rounded_rectangle(s(196, 246, 316, 276), radius=15 * SS, fill=TAN)
    d.rounded_rectangle(s(196, 290, 316, 320), radius=15 * SS, fill=TAN)
    save(img, size, "moored")


def wave(d, y, x0=104, x1=408, amp=22, width=30, color=CREAM):
    points = [(x0 + (x1 - x0) * i / 160, y + amp * math.sin(i / 160 * 4 * math.pi)) for i in range(161)]
    stroke(d, points, width, color)


def atsea():
    img, size = badge()
    d = ImageDraw.Draw(img)
    for y in (176, 256, 336):
        wave(d, y)
    save(img, size, "atsea")


def ashore():
    img, size = badge()
    d = ImageDraw.Draw(img)
    d.chord(s(110, 300, 402, 470), start=180, end=360, fill=TAN)
    stroke(d, bezier([(286, 390), (292, 300), (266, 220), (240, 176)], 80), 26, CREAM, 16)
    cx, cy = 240, 172
    fronds = [
        [(cx, cy), (180, 130), (120, 150), (100, 214)],
        [(cx, cy), (200, 100), (140, 90), (112, 110)],
        [(cx, cy), (262, 100), (320, 96), (356, 130)],
        [(cx, cy), (300, 136), (360, 160), (380, 224)],
        [(cx, cy), (232, 110), (250, 70), (286, 62)],
    ]
    for frond in fronds:
        stroke(d, bezier(frond, 80), 30, CREAM, 6)
    wave(d, 402, x0=96, x1=416, amp=10, width=22)
    save(img, size, "ashore")


def asleep():
    img, size = badge()
    d = ImageDraw.Draw(img)
    d.ellipse(s(256 - 150, 266 - 150, 256 + 150, 266 + 150), fill=CREAM)
    d.ellipse(s(318 - 136, 210 - 136, 318 + 136, 210 + 136), fill=NAVY)
    for x, y, r in ((352, 318, 16), (394, 238, 10), (300, 380, 8)):
        d.ellipse(s(x - r, y - r, x + r, y + r), fill=CREAM)
    save(img, size, "asleep")


if __name__ == "__main__":
    os.makedirs(OUT, exist_ok=True)
    large()
    anchored()
    moored()
    atsea()
    ashore()
    asleep()
