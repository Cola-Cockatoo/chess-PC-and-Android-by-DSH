"""生成简约风格的应用图标（圆角方块 + 白色骑士棋子）。

用法：python tools/make_icon.py [输出路径]
默认输出 dist/chess.ico
"""
import os
import sys

from PIL import Image, ImageDraw, ImageFont

ACCENT = (78, 124, 106, 255)
SIZES = [16, 24, 32, 48, 64, 128, 256]
FONT_PATH = r"C:\Windows\Fonts\seguisym.ttf"
GLYPH = "\u265E"  # 马


def render(size: int) -> Image.Image:
    img = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    radius = max(2, int(round(size * 0.24)))
    d.rounded_rectangle([0, 0, size - 1, size - 1], radius=radius, fill=ACCENT)

    font_size = int(round(size * 0.80))
    font = ImageFont.truetype(FONT_PATH, font_size)
    bbox = d.textbbox((0, 0), GLYPH, font=font)
    w = bbox[2] - bbox[0]
    h = bbox[3] - bbox[1]
    x = (size - w) / 2.0 - bbox[0]
    y = (size - h) / 2.0 - bbox[1]
    d.text((x, y), GLYPH, font=font, fill=(255, 255, 255, 255))
    return img


def main() -> int:
    root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    out = sys.argv[1] if len(sys.argv) > 1 else os.path.join(root, "dist", "chess.ico")
    os.makedirs(os.path.dirname(out), exist_ok=True)
    base = render(256)
    base.save(out, format="ICO", sizes=[(s, s) for s in SIZES])
    print("icon written:", out)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
