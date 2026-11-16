"""把 index.html 里 CSS 的 var(--x) 直接替换成字面量颜色。

原因：CSS 自定义属性需要 Chrome 49+，老机型上没更新的 WebView 会让整站配色失效。
直接在样式里写死颜色，任何 WebView 都能正确显示；:root 里的变量定义保留下来当作调色板说明。

（注意：不要用"在 var 行前面再插一条字面量声明"的写法 —— 那些行里有闭合大括号，
  复制一行会留下多余的 }，反而把后面的样式整段吃掉。）

用法： python inline_css_vars.py <index.html>
"""
import re
import sys

PALETTE = {
    "bg": "#F6F4F0",
    "surface": "#FFFFFF",
    "border": "#E4E0D8",
    "text": "#2C2C2A",
    "muted": "#8E8B84",
    "accent": "#4E7C6A",
    "accent-dark": "#3C6253",
    "accent-soft": "#E6EEE9",
    "light-sq": "#F1ECE2",
    "dark-sq": "#A7B69D",
    "danger": "#C25B4E",
}

VAR_RE = re.compile(r"var\(\s*--([a-z\-]+)\s*\)")


def main() -> int:
    if len(sys.argv) < 2:
        print(__doc__)
        return 2
    path = sys.argv[1]
    with open(path, "r", encoding="utf-8") as f:
        text = f.read()

    missing = []

    def sub(m):
        name = m.group(1)
        if name not in PALETTE:
            missing.append(name)
            return m.group(0)
        return PALETTE[name]

    new_text, n = VAR_RE.subn(sub, text)
    if missing:
        raise SystemExit("调色板里没有这些变量: " + ", ".join(sorted(set(missing))))
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        f.write(new_text)
    print("替换了 " + str(n) + " 处 var() -> 字面量: " + path)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
