"""把 iOS 工程打包成 zip（放进 dist，与 APK 同目录）。

注意两个跨平台细节：
  1. zip 里的路径分隔符必须是正斜杠 —— .NET 的 CreateFromDirectory 在 Windows 上会写成
     反斜杠，macOS 的 unzip 会把它当普通字符，解压出来是一堆乱名文件；
  2. 给 .sh 记上 0755 权限位，解压后可以直接 ./build_ios.sh 运行。

用法： python make_ios_zip.py
"""
import os
import shutil
import stat
import sys
import zipfile

SKIP_DIRS = {"build", "__pycache__", ".git"}


def main() -> int:
    root = os.path.dirname(os.path.abspath(__file__))
    src = os.path.join(root, "ios")
    out = os.path.join(root, "dist", "国际象棋-iOS工程.zip")
    os.makedirs(os.path.dirname(out), exist_ok=True)

    count = 0
    with zipfile.ZipFile(out, "w", zipfile.ZIP_DEFLATED) as z:
        for dirpath, dirnames, filenames in os.walk(src):
            dirnames[:] = [d for d in dirnames if d not in SKIP_DIRS]
            for name in sorted(filenames):
                if name.endswith(".pyc"):
                    continue
                full = os.path.join(dirpath, name)
                rel = os.path.relpath(full, src).replace(os.sep, "/")
                arc = "ChessIOS/" + rel
                info = zipfile.ZipInfo.from_file(full, arc)
                info.compress_type = zipfile.ZIP_DEFLATED
                if name.endswith(".sh"):
                    info.external_attr = (0o755 << 16) | stat.S_IFREG
                else:
                    info.external_attr = (0o644 << 16) | stat.S_IFREG
                with open(full, "rb") as fh, z.open(info, "w") as dst:
                    shutil.copyfileobj(fh, dst)
                count += 1

    size = os.path.getsize(out)
    print("打包 %d 个文件 -> %s  (%.1f KB)" % (count, out, size / 1024.0))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
