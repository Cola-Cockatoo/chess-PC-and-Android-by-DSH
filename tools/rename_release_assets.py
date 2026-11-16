# -*- coding: utf-8 -*-
"""把发布物复制成 ASCII 文件名（release/ 目录），并同步 README / 发布流程里的文件名。

原因：GitHub 网页上传中文文件名会被截断，实测变成了 default.apk / default.zip / -iOS.zip。
"""
import io
import os
import shutil

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

# 本地源文件 -> 上传用文件名
MAP = [
    (os.path.join(ROOT, "国际象棋电脑版.zip"), "chess-windows.zip"),
    (os.path.join(ROOT, "mobile", "dist", "国际象棋.apk"), "chess-android.apk"),
    (os.path.join(ROOT, "mobile", "dist", "国际象棋-iOS工程.zip"), "chess-ios-project.zip"),
    (os.path.join(ROOT, "mobile", "dist", "国际象棋.html"), "chess-web.html"),
]


def stage():
    out = os.path.join(ROOT, "release")
    if not os.path.isdir(out):
        os.makedirs(out)
    for src, name in MAP:
        if not os.path.isfile(src):
            print("缺少源文件: " + src)
            continue
        dst = os.path.join(out, name)
        shutil.copyfile(src, dst)
        print("%-22s %7.1f KB  <- %s" % (name, os.path.getsize(dst) / 1024.0, os.path.relpath(src, ROOT)))
    return out


def patch(path, pairs):
    full = os.path.join(ROOT, path)
    text = io.open(full, encoding="utf-8-sig").read()
    for old, new in pairs:
        if old not in text:
            raise SystemExit("锚点没找到: " + old[:40])
        text = text.replace(old, new, 1)
    io.open(full, "w", encoding="utf-8-sig", newline="").write(text)
    print("已更新 " + path)


README_PAIRS = [
    (
        "| Windows | 到 [Releases](../../releases) 下载 `国际象棋电脑版.zip`，解压后双击 `国际象棋.exe`（需 .NET Framework 4.x，Win10/11 自带） |\n"
        "| 安卓 | 到 Releases 下载 `国际象棋.apk` 安装（允许「安装未知来源应用」） |\n"
        "| iOS | 到 Releases 下载 `国际象棋-iOS工程.zip`，用 Xcode 打开工程、换成自己的签名后运行 |\n"
        "| 网页 | 到 Releases 下载 `国际象棋.html`，双击用浏览器打开即可（单文件、离线可用） |",
        "| Windows | 到 [Releases](../../releases) 下载 `chess-windows.zip`，解压后双击 `国际象棋.exe`（需 .NET Framework 4.x，Win10/11 自带） |\n"
        "| 安卓 | 到 Releases 下载 `chess-android.apk` 安装（允许「安装未知来源应用」） |\n"
        "| iOS | 到 Releases 下载 `chess-ios-project.zip`，用 Xcode 打开工程、换成自己的签名后运行 |\n"
        "| 网页 | 到 Releases 下载 `chess-web.html`，双击用浏览器打开即可（单文件、离线可用） |\n"
        "\n"
        "> 附件用英文名，是因为 GitHub 网页上传中文文件名会被截断（实测变成 `default.apk` 这类名字）。",
    ),
]

DOC_PAIRS = [
    (
        "| `国际象棋电脑版.zip` | 仓库根目录（Windows 桌面版，含使用说明） |\n"
        "| `国际象棋.apk` | `mobile\\dist\\`（安卓） |\n"
        "| `国际象棋-iOS工程.zip` | `mobile\\dist\\`（iOS 工程，需自己签名） |\n"
        "| `国际象棋.html` | `mobile\\dist\\`（网页单文件，可离线打开） |",
        "| `chess-windows.zip` | 仓库根目录（Windows 桌面版，含使用说明） |\n"
        "| `chess-android.apk` | `mobile\\dist\\`（安卓） |\n"
        "| `chess-ios-project.zip` | `mobile\\dist\\`（iOS 工程，需自己签名） |\n"
        "| `chess-web.html` | `mobile\\dist\\`（网页单文件，可离线打开） |\n"
        "\n"
        "> **必须用英文名上传。** GitHub 网页上传中文文件名会被截断，实测 `国际象棋.apk` 变成了 `default.apk`、\n"
        "> `国际象棋-iOS工程.zip` 变成了 `-iOS.zip`。先跑 `python tools\\rename_release_assets.py`，\n"
        "> 它会把四个产物复制到 `release\\` 目录并改成英文名，再把那个目录里的文件拖进 Release。",
    ),
]


if __name__ == "__main__":
    stage()
    patch("README.md", README_PAIRS)
    patch(os.path.join("docs", "发布流程.md"), DOC_PAIRS)
    print("\n完成：把 release\\ 目录里的 4 个文件拖进 GitHub Release 即可。")
