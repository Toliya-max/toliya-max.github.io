import io
import struct
from PIL import Image, ImageDraw, ImageFont

BG_COLOR = (14, 11, 7)
ACCENT = (212, 152, 90)
ACCENT_DARK = (140, 95, 50)
ACCENT_GLOW = (232, 184, 122)

KNIGHT_POINTS = [
    (0.50, 0.12), (0.42, 0.15), (0.35, 0.22), (0.30, 0.18),
    (0.22, 0.20), (0.25, 0.28), (0.20, 0.35), (0.18, 0.45),
    (0.22, 0.50), (0.28, 0.48), (0.30, 0.52), (0.25, 0.60),
    (0.22, 0.70), (0.25, 0.78), (0.30, 0.82), (0.28, 0.88),
    (0.72, 0.88), (0.70, 0.82), (0.65, 0.75), (0.60, 0.62),
    (0.58, 0.52), (0.62, 0.45), (0.65, 0.38), (0.62, 0.30),
    (0.58, 0.22), (0.55, 0.16),
]

EYE_POS = (0.36, 0.36)
EYE_R = 0.025


def draw_knight(size):
    img = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    draw = ImageDraw.Draw(img)

    pad = int(size * 0.08)
    r = int(size * 0.18)
    draw.rounded_rectangle([pad, pad, size - pad, size - pad], radius=r, fill=BG_COLOR)

    border_w = max(1, size // 64)
    draw.rounded_rectangle(
        [pad, pad, size - pad, size - pad],
        radius=r, outline=ACCENT_DARK, width=border_w,
    )

    m = int(size * 0.1)
    area = size - 2 * m
    points = [(int(m + x * area), int(m + y * area)) for x, y in KNIGHT_POINTS]

    draw.polygon(points, fill=ACCENT)

    highlight_pts = points[:len(points) // 2]
    if len(highlight_pts) > 2:
        draw.line(highlight_pts, fill=ACCENT_GLOW, width=max(1, size // 80))

    ex = int(m + EYE_POS[0] * area)
    ey = int(m + EYE_POS[1] * area)
    er = max(1, int(EYE_R * area))
    draw.ellipse([ex - er, ey - er, ex + er, ey + er], fill=BG_COLOR)

    return img


def create_ico(path, sizes=(256, 128, 64, 48, 32, 16)):
    images = []
    for s in sizes:
        img = draw_knight(s)
        images.append(img)

    buf = io.BytesIO()
    images[0].save(buf, format="ICO", sizes=[(s, s) for s in sizes], append_images=images[1:])

    with open(path, "wb") as f:
        f.write(buf.getvalue())
    print(f"Icon saved: {path} ({len(buf.getvalue())} bytes)")


if __name__ == "__main__":
    create_ico("LichessBotGUI/app.ico")
    create_ico("LichessBotSetup/app.ico")
    print("Done!")
