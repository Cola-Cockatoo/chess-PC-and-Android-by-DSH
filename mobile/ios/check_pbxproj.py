"""静态校验手写的 project.pbxproj（本机没有 macOS/Xcode，无法真正编译）。

检查项：
  1. 大括号 / 圆括号是否配平；
  2. 所有被引用的 24 位 UUID 是否都有定义（这是 Xcode 报"工程已损坏"的最常见原因）；
  3. 必需对象是否齐全：PBXProject / PBXNativeTarget / 源码与资源构建阶段 / 两份配置列表；
  4. 目标里引用的文件是否都真实存在（swift / 资源 / Info.plist / www）。

用法： python check_pbxproj.py ..\ios\ChessIOS.xcodeproj\project.pbxproj ..\ios
"""
import os
import re
import sys

UUID_RE = re.compile(r"\b[0-9A-F]{24}\b")
DEF_RE = re.compile(r"^\s*([0-9A-F]{24})(?:\s*/\*.*?\*/)?\s*=\s*\{", re.M)
ISA_RE = re.compile(r"isa\s*=\s*(\w+)\s*;")


def main() -> int:
    if len(sys.argv) < 3:
        print(__doc__)
        return 2
    proj, root = sys.argv[1], sys.argv[2]
    text = open(proj, encoding="utf-8").read()
    problems = []

    # 1) 配平
    if text.count("{") != text.count("}"):
        problems.append("大括号不配平：{ %d 个，} %d 个" % (text.count("{"), text.count("}")))
    if text.count("(") != text.count(")"):
        problems.append("圆括号不配平")

    # 2) UUID 引用都有定义
    defined = set(DEF_RE.findall(text))
    referenced = set(UUID_RE.findall(text))
    missing = sorted(u for u in referenced if u not in defined)
    if missing:
        problems.append("有引用但没有定义的对象：" + ", ".join(missing))

    # 3) 必需对象
    isas = set(ISA_RE.findall(text))
    for need in ("PBXProject", "PBXNativeTarget", "PBXSourcesBuildPhase",
                 "PBXResourcesBuildPhase", "XCBuildConfiguration", "XCConfigurationList",
                 "PBXFileReference", "PBXBuildFile", "PBXGroup"):
        if need not in isas:
            problems.append("缺少 " + need)
    if "rootObject" not in text:
        problems.append("缺少 rootObject")
    root_uuid = re.search(r"rootObject\s*=\s*([0-9A-F]{24})", text)
    if not root_uuid or root_uuid.group(1) not in defined:
        problems.append("rootObject 指向的对象不存在")

    # 4) 引用的文件真实存在
    checks = [
        ("ChessIOSApp.swift", "PBXFileReference; lastKnownFileType = sourcecode.swift"),
        ("WebView.swift", "PBXFileReference; lastKnownFileType = sourcecode.swift"),
    ]
    for name, _ in checks:
        if not os.path.isfile(os.path.join(root, "ChessIOS", name)):
            problems.append("找不到源文件 " + name)
    for rel in ("ChessIOS/Info.plist",
                "ChessIOS/Assets.xcassets/AppIcon.appiconset/icon-1024.png",
                "ChessIOS/Assets.xcassets/AppIcon.appiconset/Contents.json",
                "ChessIOS/Assets.xcassets/AccentColor.colorset/Contents.json",
                "ChessIOS/www/index.html"):
        if not os.path.isfile(os.path.join(root, rel)):
            problems.append("找不到资源 " + rel)

    # 5) 关键构建设置
    for key in ("INFOPLIST_FILE", "PRODUCT_BUNDLE_IDENTIFIER", "SWIFT_VERSION",
                "IPHONEOS_DEPLOYMENT_TARGET", "ASSETCATALOG_COMPILER_APPICON_NAME"):
        if key not in text:
            problems.append("构建配置里缺少 " + key)

    print("对象定义 %d 个，引用 %d 个" % (len(defined), len(referenced)))
    print("对象类型：" + ", ".join(sorted(isas)))
    if problems:
        print("\n发现问题：")
        for p in problems:
            print("  - " + p)
        return 1
    print("\nPASS：结构自洽（UUID 引用完整、构建阶段与配置齐全、文件均存在）")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
