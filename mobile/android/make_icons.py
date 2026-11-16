"""生成 Android 启动图标（与桌面版同一套设计：鼠尾草绿圆角方块 + 白色骑士）。

用法： python make_icons.py
输出： app/src/main/res/mipmap-*/ic_launcher.png
"""
import os
import sys

from PIL import Image, ImageDraw, ImageFont

ACCENT = (78, 124, 106, 255)
GLYPH = "\u265E"  # 马
DENSITIES = {
    "mdpi": 48,
    "hdpi": 72,
    "xhdpi": 96,
    "xxhdpi": 144,
    "xxxhdpi": 192,
}
FONT_CANDIDATES = [
    r"C:\Windows\Fonts\seguisym.ttf",
    r"C:\Windows\Fonts\msgothic.ttc",
]


def find_font():
    for p in FONT_CANDIDATES:
        if os.path.exists(p):
            return p
    raise SystemExit("找不到带国际象棋符号的字体")


def render(size: int, font_path: str) -> Image.Image:
    img = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    radius = max(2, int(round(size * 0.22)))
    d.rounded_rectangle([0, 0, size - 1, size - 1], radius=radius, fill=ACCENT)
    font = ImageFont.truetype(font_path, int(round(size * 0.78)))
    bbox = d.textbbox((0, 0), GLYPH, font=font)
    w, h = bbox[2] - bbox[0], bbox[3] - bbox[1]
    d.text(((size - w) / 2.0 - bbox[0], (size - h) / 2.0 - bbox[1]), GLYPH, font=font,
           fill=(255, 255, 255, 255))
    return img


def main() -> int:
    root = os.path.dirname(os.path.abspath(__file__))
    res = os.path.join(root, "app", "src", "main", "res")
    font_path = find_font()
    for name, px in DENSITIES.items():
        out_dir = os.path.join(res, "mipmap-" + name)
        os.makedirs(out_dir, exist_ok=True)
        out = os.path.join(out_dir, "ic_launcher.png")
        render(px, font_path).save(out, format="PNG")
        print("icon:", out, px, "px")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
