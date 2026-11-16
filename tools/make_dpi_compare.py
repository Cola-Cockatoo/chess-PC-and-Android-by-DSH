"""生成"文字发虚"的对照图，用来确认 DPI 感知修复的效果。

左/上：新版（声明 DPI 感知）——窗口 1410×1050，界面按 1.5 倍原生排版，字由 GDI+ 直接画大。
右/下：旧版效果——窗口 940×700 渲染完，被 Windows 整体拉伸 1.5 倍（模拟旧行为）。

用法： python make_dpi_compare.py
输出： dev_shots/23_dpi_compare.png
"""
import os
import sys

from PIL import Image, ImageDraw, ImageFont

FONT = r"C:\Windows\Fonts\msyh.ttc"
CROP = (330, 56, 1110, 300)          # 标题 + 副标题 + 第一行档位
SEP = 6


def label_font(size=26):
    return ImageFont.truetype(FONT, size)


def main() -> int:
    root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    shots = os.path.join(root, "dev_shots")
    native = Image.open(os.path.join(shots, "22_difficulty_native_150pct.png")).convert("RGB")
    base = Image.open(os.path.join(shots, "02_difficulty.png")).convert("RGB")

    # 旧行为：940×700 画完后被系统拉伸到 1410×1050
    stretched = base.resize(native.size, Image.BICUBIC)

    a = native.crop(CROP)
    b = stretched.crop(CROP)

    w = CROP[2] - CROP[0]
    h = CROP[3] - CROP[1]
    pad = 12
    out = Image.new("RGB", (w + pad * 2, pad * 3 + h * 2 + 34 * 2), (0xFF, 0xFF, 0xFF))
    d = ImageDraw.Draw(out)
    f = label_font(24)

    d.text((pad, pad), "新版：声明 DPI 感知，按 150% 原生渲染（清晰）", font=f, fill=(0x2C, 0x2C, 0x2A))
    out.paste(a, (pad, pad + 34))
    y2 = pad + 34 + h + pad
    d.text((pad, y2), "旧版：940×700 画完被系统拉伸 150%（发虚）", font=f, fill=(0xC2, 0x5B, 0x4E))
    out.paste(b, (pad, y2 + 34))

    dst = os.path.join(shots, "23_dpi_compare.png")
    out.save(dst)
    print("对照图:", dst, out.size)
    return 0


if __name__ == "__main__":
    sys.exit(main())
