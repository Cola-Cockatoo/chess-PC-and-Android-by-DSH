"""把 classes.dex 与网页资源打进 aapt2 产出的 APK。

为什么要单独写这一步：
    .NET 的 ZipArchive（Update 模式）重写 aapt2 生成的压缩包时，会把带 data descriptor
    的条目（AndroidManifest.xml / resources.arsc）写坏，aapt2 再读就报
    "size/crc32 mismatch"。Python 的 zipfile 按中央目录重写，行为可靠。

用法： pack_apk.py <aapt2产出.apk> <classes.dex> <assets目录> <输出.apk>
（第三个参数给单个文件也兼容，会当作 assets/index.html）
"""
import os
import sys
import zipfile


def asset_entries(assets_arg):
    """返回 [(磁盘路径, APK 内路径)]；给目录就递归打包，给文件就当作 index.html。"""
    if os.path.isdir(assets_arg):
        out = []
        for dirpath, _dirnames, filenames in os.walk(assets_arg):
            for name in sorted(filenames):
                full = os.path.join(dirpath, name)
                rel = os.path.relpath(full, assets_arg).replace(os.sep, "/")
                out.append((full, "assets/" + rel))
        return out
    return [(assets_arg, "assets/index.html")]


def main() -> int:
    if len(sys.argv) < 5:
        print(__doc__)
        return 2
    src_apk, dex, assets_arg, out_apk = sys.argv[1:5]

    zin = zipfile.ZipFile(src_apk, "r")
    zout = zipfile.ZipFile(out_apk, "w")
    try:
        for item in zin.infolist():
            data = zin.read(item.filename)
            zi = zipfile.ZipInfo(item.filename, date_time=item.date_time)
            # 保持原压缩方式（aapt2 对 resources.arsc 用 STORED，Android 11+ 也要求如此）
            zi.compress_type = item.compress_type
            zi.external_attr = item.external_attr
            zi.internal_attr = item.internal_attr
            zi.create_system = item.create_system
            zout.writestr(zi, data)
        # 新增：dex 与网页资源（assets 目录下有啥打啥，iOS 图标也一起带上）
        zout.write(dex, "classes.dex", zipfile.ZIP_DEFLATED)
        for src, arc in asset_entries(assets_arg):
            zout.write(src, arc, zipfile.ZIP_DEFLATED)
    finally:
        zout.close()
        zin.close()
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
