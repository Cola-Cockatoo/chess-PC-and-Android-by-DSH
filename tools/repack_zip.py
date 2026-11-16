# -*- coding: utf-8 -*-
"""重打交付压缩包：python tools/repack_zip.py"""
import io
import os
import shutil
import sys
import zipfile

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
EXE = os.path.join(ROOT, "dist", "国际象棋.exe")
HTML = os.path.join(ROOT, "mobile", "dist", "国际象棋.html")
STAGE_ROOT = os.path.join(ROOT, "dist", "_zip_stage")
STAGE = os.path.join(STAGE_ROOT, "国际象棋电脑版")
ZIP = os.path.join(ROOT, "国际象棋电脑版.zip")
FB = "备用网页版（仅在 exe 无法运行时用）"

TXT = """国际象棋（电脑版）使用说明
================================

★★★ 解压整个文件夹 → 双击「国际象棋.exe」 ★★★

【被 Windows 拦住？】本程序无代码签名：SmartScreen 提示时点「更多信息」→「仍要运行」。

【玩什么】
· 人机对战（ELO 100~2000，每 100 一档，可选执白/执黑）／人人对战（本地，自动翻面）／定式学习（25 个开局）
· 对局界面：棋盘左侧行棋记录，右上角「控制格」「AI 解析」开关
· 控制格：棋盘整块上色（黑方控制=黑、白方控制=白、双方=浅灰、都没=浅绿）
· 对局结束后，「新局」左边出现「复盘」按钮（与新局同尺寸）：
  自动把棋走一遍，每手只给一句"这手干了什么"；关键步停下点评；走坏的那几步退回让你重下，
  答对后真的把棋走上去、停下来等你点「继续」。
· 复盘界面：「◀ 上一步」把棋盘和对话框一起退回上一手（对话逐行还原，不会追加废话）；
  「下一个关键步 ▶」跳着看；可切换「控制格」「AI 解析」；文本框高度固定、内容自动向下滚动。
· 快捷键：F11 全屏 / Esc 退出；复盘里 空格=继续、←=上一步、→=下一个关键步

【系统要求】Windows 7/8/10/11 + .NET Framework 4.0 以上（Win10/11 自带 4.8）。

【备用网页版】只在 exe 无法运行时用：双击「@F@/国际象棋.html」（Edge/Chrome 打开）。
网页版同样有复盘；宽屏自动排成「左记录 · 中棋盘 · 右解说」。

【卸载】删掉整个文件夹即可。
"""


def add_dir(zf, src, prefix):
    for dirpath, dirnames, filenames in os.walk(src):
        dirnames.sort()
        for fn in sorted(filenames):
            full = os.path.join(dirpath, fn)
            zf.write(full, prefix + "/" + os.path.relpath(full, src).replace("\\", "/"))


def main() -> int:
    if os.path.isdir(STAGE_ROOT):
        shutil.rmtree(STAGE_ROOT)
    os.makedirs(os.path.join(STAGE, FB))
    shutil.copyfile(EXE, os.path.join(STAGE, "国际象棋.exe"))
    if os.path.isfile(HTML):
        shutil.copyfile(HTML, os.path.join(STAGE, FB, "国际象棋.html"))
    txt = TXT.replace("@F@", FB)
    assert "@F@" not in txt
    with io.open(os.path.join(STAGE, "使用说明.txt"), "w", encoding="utf-8-sig", newline="\r\n") as f:
        f.write(txt)
    if os.path.isfile(ZIP):
        os.remove(ZIP)
    with zipfile.ZipFile(ZIP, "w", zipfile.ZIP_DEFLATED, compresslevel=9) as zf:
        add_dir(zf, STAGE, "国际象棋电脑版")
    shutil.rmtree(STAGE_ROOT)
    print("已生成 %s  %.1f KB" % (ZIP, os.path.getsize(ZIP) / 1024.0))
    with zipfile.ZipFile(ZIP) as zf:
        for i in zf.infolist():
            print("  %-50s %8.1f KB" % (i.filename, i.file_size / 1024.0))
        print("CRC:", "通过" if zf.testzip() is None else "损坏")
    return 0


if __name__ == "__main__":
    sys.exit(main())
