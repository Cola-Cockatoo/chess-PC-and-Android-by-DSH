# 国际象棋 · iOS 版（iPhone / iPad）

## 先说清楚一件事

**`.ipa` 安装包没法在这台 Windows 机器上生成**，这不是愿不愿意做的问题：

| 生成 `.ipa` 需要什么 | 这台机器 |
| --- | --- |
| macOS + Xcode（含 iOS SDK，用来把代码编译成 arm64 的 iOS 可执行文件） | ✗ 只有 Windows |
| Apple 开发者签名身份（证书 + 描述文件，否则 iOS 拒绝安装） | ✗ 必须由 Apple 签发 |
| 免费 Apple ID | 只能在自己设备上装，且 7 天后失效 |

所以这里给的是**两条真正能走通的路**：

- **路线 A（不用 Mac，今天就能在 iPhone 上玩）**：用网页版，Safari 里"添加到主屏幕"，
  桌面会出现一个「国际象棋」图标，点开就是全屏、和 App 一样。
- **路线 B（有 Mac 时）**：本目录是一个**现成的 Xcode 工程**，打开就能编译出真正的 IPA。

---

## 路线 A：iPhone / iPad 上用网页版（不需要 Mac）

手机上真正需要的只有两个文件（**必须同一目录**）：`国际象棋.html` + `apple-touch-icon.png`。
`.apk` 是安卓包（iPhone 装不了），本目录的 Xcode 工程是给 Mac 的 —— 都不要往手机里传。

**最省事的做法：一个文件都不用传。**

1. 让电脑与 iPhone 连同一个 Wi-Fi；
2. 打开 `mobile/dist`，右键 **`启动手机版服务器.ps1`** → *使用 PowerShell 运行*
   （不需要 Python / Node / 管理员权限；首次运行 Windows 会弹防火墙提示，勾选"专用网络"并允许）；
3. 窗口里会打印形如 `http://192.168.1.5:8000/` 的网址（有多个网卡时会列出备选），
   iPhone 用 **Safari** 打开它 → 点下方 **分享** → **添加到主屏幕** → 添加；
4. 桌面出现「国际象棋」图标，点开即全屏运行
   （页面里已加 `apple-mobile-web-app-capable` 等 iOS 专用声明）。

> **这个图标指向的是那个网址**：电脑关掉、或手机离开同一个 Wi-Fi 时就打不开。
> 想随时随地打开，就把这两个文件放到一个一直在线的地方（任意静态托管 / 对象存储 / 网盘直链），
> 用 https 链接打开后再"添加到主屏幕"。

> 页面已做 iOS 安全区适配（`viewport-fit=cover` + `env(safe-area-inset-*)`，带老浏览器兜底），
> 不会挡到灵动岛或底部小白条；在 Safari 里（未添加到主屏幕时）主页底部会提示一次，
> 添加后自动消失。

> 顺带说明：把 `.html` 隔空投送/微信发到手机、用「文件」App 打开，**能预览但装不了图标** ——
> iOS 只允许从 Safari 的网址页面"添加到主屏幕"。所以想装成图标请用上面的网址方式。

## 路线 B：用 Xcode 编译真正的 App / IPA（需要 Mac）

> **这条路对 iPhone 有用，但前提是"手边有一台 Mac"** —— 编译和签名都在 Mac 上完成，
> iPhone 只是被安装的目标。没有 Mac 的话这条路走不通，请用路线 A。
> 好消息是：编出来的 App **完全离线可用**，不依赖电脑、不依赖网址，点开就玩。

1. Mac 上装 Xcode（App Store 免费；Xcode 15 需要 macOS 13.5+，Xcode 16 需要 macOS 14.5+），
   执行过一次 `sudo xcode-select --switch /Applications/Xcode.app`；
2. 双击 `ChessIOS.xcodeproj`（或 `open ChessIOS.xcodeproj`）；
3. Xcode 里选中 ChessIOS 工程 → **Signing & Capabilities** → 勾上 *Automatically manage signing* →
   **Team** 选你的 Apple ID（Xcode → Settings → Accounts 里先登录，**免费账号即可**）；
4. 用**数据线**把 iPhone 连到 Mac（首次连接手机上要点"信任此电脑"），
   Xcode 顶部设备列表选中你的 iPhone，按 **⌘R**；
5. 手机上还要做两件事，否则 App 打不开：
   - **信任开发者**：设置 → 通用 → VPN 与设备管理 → 开发者 App → 点你的 Apple ID → 信任；
   - **开发者模式**（iOS 16 及以上）：设置 → 隐私与安全性 → 开发者模式 → 打开 → 重启手机。
6. 想产出 `.ipa` 文件：
   ```bash
   cd mobile/ios
   TEAM=你的团队ID ./build_ios.sh ipa      # 会生成 mobile/ios/build/ipa/国际象棋.ipa
   ./build_ios.sh sim                      # 只想在模拟器里看看效果（不需要任何账号）
   ```

**免费 Apple ID 的限制（务必知道）**

| 限制 | 说明 |
| --- | --- |
| 有效期 **7 天** | 到期后 App 打不开，需要再连 Mac 按一次 ⌘R 重新安装 |
| 只能自己的设备 | 免费账号签的 App 无法发给别人装（要发给别人得用付费账号 + TestFlight/Ad Hoc） |
| 需要 Mac 在场 | 每次续期都要连 Mac；电脑不在身边就没法续 |
| `.ipa` 不能直接传到手机 | iOS 不支持"点开 ipa 安装"；`.ipa` 只能用 Xcode 的 *Devices* 窗口、Apple Configurator 2 或 AltStore/Sideloadly 这类工具（会重新用你的 Apple ID 签名）安装 |

**想要"装上就一直能用"**：加入 **付费开发者账号**（99 美元/年），
用 Ad Hoc 分发（有效期 1 年）或 TestFlight（90 天）；上架 App Store 则永久。
另外要把 `PRODUCT_BUNDLE_IDENTIFIER`（现为 `com.dsh.chess`）改成你自己的唯一 ID。

**系统要求**：工程的最低系统是 **iOS 14.0**（iPhone 6s 及以后、iPad 同理），
App 体积约 200 KB，不联网、不申请任何权限。

---

## 工程里有什么

```
mobile/ios/
├─ ChessIOS.xcodeproj/project.pbxproj     手写的工程文件（结构已静态校验）
├─ ChessIOS/
│  ├─ ChessIOSApp.swift                   SwiftUI 入口
│  ├─ WebView.swift                       WKWebView 外壳（加载 www/index.html）
│  ├─ Info.plist                          显示名「国际象棋」、竖横屏、无网络权限、启动屏
│  ├─ Assets.xcassets/                    App 图标（1024×1024，不透明）+ 主题色
│  └─ www/index.html                      游戏本体：与安卓/网页同一份单文件 HTML
│     └─ apple-touch-icon.png             图标（同目录引用）
├─ build_ios.sh                           构建脚本（sim / ipa 两种模式）
├─ ExportOptions.plist                    导出 ipa 用的配置
└─ check_pbxproj.py                       工程文件静态校验（本机无 Xcode 时的替代手段）
```

`www/index.html` 由 `mobile/build_web.ps1` 生成（与 APK 内那份、`mobile/dist/国际象棋.html`
**逐字节一致**）—— 三端同一份游戏代码，改一处全都受益。

### iOS 外壳做了什么

- WKWebView 只加载打包进来的本地文件：`loadFileURL(_:allowingReadAccessTo:)` 把读取范围限制在 `www/`；
- **不加任何 JS 桥**、不联网（Info.plist 里 `NSAllowsArbitraryLoads = false`），
  页面本身也没有任何外部请求 —— 与安卓版同样的安全取向；
- WebView 落在安全区内（不 `ignoresSafeArea`），刘海/灵动岛/底部指示条都不会遮挡内容；
- 支持竖屏与横屏，旋转由页面的 resize 逻辑接管，棋局不会丢。

---

## 如果 Xcode 打不开这个工程（兜底办法，约 5 分钟）

手写的 `project.pbxproj` 已经过结构校验（UUID 引用完整、构建阶段与配置齐全），
但毕竟没在真实 Xcode 里编译过。万一你的 Xcode 版本不认它，用这个办法：

1. Xcode → **File → New → Project → iOS → App**；
   Product Name 填 `ChessIOS`，Interface 选 **SwiftUI**，Language 选 **Swift**，保存到任意位置；
2. 把 `mobile/ios/ChessIOS/ChessIOSApp.swift` 与 `WebView.swift` 拖进新工程（覆盖同名文件），
   勾选 *Copy items if needed*；
3. 把 `mobile/ios/ChessIOS/www` 整个文件夹拖进新工程，弹窗选 **Create folder references**（蓝色文件夹图标，
   这样 www 会原样拷进 App）；
4. 把 `mobile/ios/ChessIOS/Assets.xcassets/AppIcon.appiconset/icon-1024.png`
   拖到 AppIcon 槽位；
5. Target → General → Display Name 填「国际象棋」；
   Target → Info 里加上 `UILaunchScreen`（空字典）、
   `UISupportedInterfaceOrientations` 选竖屏 + 左右横屏；
6. 按 ⌘R 运行。

## 验证情况（如实说明）

| 检查 | 结果 |
| --- | --- |
| `project.pbxproj` 结构 | `check_pbxproj.py` **PASS**：24 个对象全部有定义、UUID 引用无悬空、源码/资源构建阶段与两份配置列表齐全、引用的文件都存在 |
| `Info.plist` / `ExportOptions.plist` | XML 解析通过 |
| 资源目录三个 `Contents.json` | JSON 解析通过 |
| App 图标 | 1024×1024、24bpp 不透明（iOS 要求图标不含透明通道） |
| 页面本体 | 与已在安卓/浏览器验证过的 `mobile/dist/国际象棋.html` 逐字节一致 |
| **能否编译** | **未验证** —— 本机没有 macOS/Xcode，这条只能在你的 Mac 上确认 |

如果有任何一步在 Xcode 里报错，把报错原文发我，我按你的 Xcode 版本改工程文件。
