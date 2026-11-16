"""生成 iOS 主屏图标（apple-touch-icon.png，180×180）。

iOS 的要求与安卓不同：
  * 图标必须是**不透明**的整块方图（透明处会被系统填成黑色）；
  * 圆角与高光由 iOS 自己裁切，所以这里画满整块，不要自己做圆角。

用法： python make_ios_icon.py
输出： web/apple-touch-icon.png
"""
import os
import sys

from PIL import Image, ImageDraw, ImageFont

ACCENT = (78, 124, 106, 255)
GLYPH = "\u265E"   # 马
SIZE = 180
FONT_CANDIDATES = [
    r"C:\Windows\Fonts\seguisym.ttf",
    r"C:\Windows\Fonts\msgothic.ttc",
]


def find_font() -> str:
    for p in FONT_CANDIDATES:
        if os.path.exists(p):
            return p
    raise SystemExit("找不到带国际象棋符号的字体")


def render(size: int, font_path: str) -> Image.Image:
    """整块不透明方图（圆角由 iOS 自己裁）。"""
    img = Image.new("RGB", (size, size), ACCENT)
    d = ImageDraw.Draw(img)
    font = ImageFont.truetype(font_path, int(round(size * 0.74)))
    bbox = d.textbbox((0, 0), GLYPH, font=font)
    w, h = bbox[2] - bbox[0], bbox[3] - bbox[1]
    d.text(((size - w) / 2.0 - bbox[0], (size - h) / 2.0 - bbox[1]), GLYPH, font=font,
           fill=(255, 255, 255))
    return img


def main() -> int:
    root = os.path.dirname(os.path.abspath(__file__))
    font_path = find_font()

    # 1) 网页版（Safari"添加到主屏幕"）用 180×180
    out = os.path.join(root, "web", "apple-touch-icon.png")
    render(SIZE, font_path).save(out, format="PNG")
    print("web icon:", out, SIZE, "px")

    # 2) Xcode 工程里的 App 图标（1024×1024）
    appicon = os.path.join(root, "ios", "ChessIOS", "Assets.xcassets",
                           "AppIcon.appiconset", "icon-1024.png")
    os.makedirs(os.path.dirname(appicon), exist_ok=True)
    render(1024, font_path).save(appicon, format="PNG")
    print("app icon:", appicon, "1024 px")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
