"""把桌面版所有小于 15 的字号抬到更高的下限（最小 12.5），15 及以上不动。

只改"字号位置"的数字：Theme.Font(x, ...) / UiScale.Font(ctrl, x, ...) / Ui.MakeLabel(..., x, ...)。
用法： python bump_font_sizes.py [--check]
"""
import io
import os
import re
import sys

# 旧字号 -> 新字号（15 及以上保持不变）
MAP = {
    8.0: 12.5, 8.5: 12.5, 9.0: 12.5, 9.5: 12.5, 10.0: 12.5,
    10.5: 13.0, 11.0: 13.0, 11.5: 13.5, 12.0: 13.5, 12.5: 14.0,
    13.0: 14.5, 13.5: 15.0, 14.0: 15.0,
}

PATTERNS = [
    re.compile(r"(Theme\.Font\(\s*)(\d+(?:\.\d+)?)(f\s*,)"),
    re.compile(r"(UiScale\.Font\(\s*[A-Za-z_][\w.]*\s*,\s*)(\d+(?:\.\d+)?)(f\s*,)"),
    re.compile(r"(Ui\.MakeLabel\(\s*\"[^\"]*\"\s*,\s*)(\d+(?:\.\d+)?)(f\s*,)"),
]


def bump(value: str):
    v = float(value)
    for old, new in MAP.items():
        if abs(v - old) < 1e-6:
            return new
    return None


def main() -> int:
    check = "--check" in sys.argv
    root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    ui = os.path.join(root, "src", "UI")
    changed_files = 0
    changed_hits = 0

    for name in sorted(os.listdir(ui)):
        if not name.endswith(".cs"):
            continue
        path = os.path.join(ui, name)
        with io.open(path, encoding="utf-8") as f:
            text = f.read()
        original = text

        def repl(m):
            nonlocal changed_hits
            new = bump(m.group(2))
            if new is None:
                return m.group(0)
            changed_hits += 1
            return m.group(1) + ("%g" % new) + m.group(3)

        for pat in PATTERNS:
            text = pat.sub(repl, text)

        if text != original:
            changed_files += 1
            if not check:
                with io.open(path, "w", encoding="utf-8", newline="\n") as f:
                    f.write(text)
            print("  " + name + " 已更新")

    print("共 %d 个文件、%d 处字号%s" % (changed_files, changed_hits, "（仅检查）" if check else ""))
    return 0


if __name__ == "__main__":
    sys.exit(main())
