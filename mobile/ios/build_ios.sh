#!/bin/bash
# 在 macOS 上构建国际象棋 iOS 版
#
#   ./build_ios.sh sim     只给模拟器构建（不需要 Apple 账号，用来先看看效果）
#   ./build_ios.sh ipa     构建真机版并打包成 .ipa（需要先在 Xcode 里登录 Apple ID 并选好 Team）
#
# 前置：装好 Xcode（App Store 免费下载），并执行过一次
#   sudo xcode-select --switch /Applications/Xcode.app
set -euo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
PROJ="$HERE/ChessIOS.xcodeproj"
WWW="$HERE/ChessIOS/www"
MODE="${1:-sim}"

# ---- 1) 准备 www/index.html（网页版单文件是唯一真源，直接拷进来）----
if [ ! -f "$WWW/index.html" ]; then
  mkdir -p "$WWW"
  SRC="$HERE/../dist/国际象棋.html"
  if [ -f "$SRC" ]; then
    cp "$SRC" "$WWW/index.html"
    echo "已从 dist 复制网页版 -> www/index.html"
  else
    echo "缺少 www/index.html：请先运行 mobile/build_web.ps1 生成单文件网页版" >&2
    exit 1
  fi
fi
if [ -f "$HERE/../dist/apple-touch-icon.png" ] && [ ! -f "$WWW/apple-touch-icon.png" ]; then
  cp "$HERE/../dist/apple-touch-icon.png" "$WWW/apple-touch-icon.png"
fi

# ---- 2) 构建 ----
case "$MODE" in
  sim)
    echo "== 构建模拟器版本 =="
    xcodebuild -project "$PROJ" -target ChessIOS -configuration Debug \
      -sdk iphonesimulator -arch arm64 -arch x86_64 \
      SYMROOT="$HERE/build" build
    APP="$HERE/build/Debug-iphonesimulator/ChessIOS.app"
    echo
    echo "完成：$APP"
    echo "运行方式：打开 Xcode -> Open -> 选 ChessIOS.xcodeproj -> 顶部选一个 iPhone 模拟器 -> ⌘R"
    ;;
  ipa)
    echo "== 构建真机版本（Release）=="
    if [ -z "${TEAM:-}" ]; then
      echo "提示：没有设置 TEAM 环境变量，将使用 Xcode 里已配置的签名。"
      echo "      若报签名错误，请先用 Xcode 打开工程，在 Signing & Capabilities 里选好 Team，"
      echo "      或者这样跑：TEAM=你的团队ID ./build_ios.sh ipa"
    fi
    xcodebuild -project "$PROJ" -target ChessIOS -configuration Release \
      -sdk iphoneos SYMROOT="$HERE/build" \
      ${TEAM:+DEVELOPMENT_TEAM="$TEAM"} \
      CODE_SIGN_STYLE=Automatic build

    APP="$HERE/build/Release-iphoneos/ChessIOS.app"
    [ -d "$APP" ] || { echo "没有生成 $APP" >&2; exit 1; }

    echo "== 打包 .ipa =="
    rm -rf "$HERE/build/ipa" && mkdir -p "$HERE/build/ipa/Payload"
    cp -R "$APP" "$HERE/build/ipa/Payload/"
    (cd "$HERE/build/ipa" && zip -qry "国际象棋.ipa" Payload)
    rm -rf "$HERE/build/ipa/Payload"
    echo
    echo "完成：$HERE/build/ipa/国际象棋.ipa"
    echo "安装：用 Xcode（Window -> Devices and Simulators）把 .ipa 拖进去，或用 Apple Configurator。"
    echo "注意：免费 Apple 账号签的 App 只能在自己设备上运行，且 7 天后需要重新安装。"
    ;;
  *)
    echo "用法： $0 [sim|ipa]" >&2
    exit 2
    ;;
esac
