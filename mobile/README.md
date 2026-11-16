# 国际象棋 · 手机版（Android）

手机版与桌面版同一套规则与引擎，界面换成适合触屏的布局。

| 产物 | 说明 |
| --- | --- |
| `dist/国际象棋.apk` | **安卓安装包**（80.9 KB），传到手机点开即可安装 |
| `dist/国际象棋.html` | 单文件网页版（144 KB），用手机浏览器打开也能玩，可"添加到主屏幕"当 App 用 |

> **苹果（iOS）端**：.ipa 需要 macOS + Xcode + Apple 签名，无法在本机生成。
> 可行的两条路（网页版加到主屏幕 / 现成 Xcode 工程）见 [ios/README.md](ios/README.md)，
> 相关文件也在同一个 dist/ 目录里：国际象棋.html、apple-touch-icon.png、国际象棋-iOS工程.zip、iOS安装说明.txt。

> 本机（构建环境）没有网络也没有 Android SDK，APK 是用国内 HTTP 镜像下载的
> OpenJDK + build-tools + platform，再用 `aapt2 / javac / d8 / apksigner` 手工打包出来的，
> 整个过程可复现：见下面「自己重新构建」。

---

## 1. 安装与使用

**装 APK**

1. 把 `dist/国际象棋.apk` 传到手机（数据线 / 微信文件传输 / 网盘都行）；
2. 在手机上点开它，系统会提示"是否允许安装未知来源应用"，允许后即可安装；
3. 桌面出现「国际象棋」图标，点开就能玩。

安装包**没有申请任何权限**（连网络权限都没有），完全离线运行；安装时提示的权限列表是空的。

**或者不装 APK**：直接把 `dist/国际象棋.html` 传到手机，用 Chrome / 系统浏览器打开；
在浏览器菜单里选「添加到主屏幕」，也会得到一个图标，效果与 APK 基本一致。

### 兼容性

- **Android 5.0 (API 21) 及以上**，targetSdk 29；覆盖在用的绝大多数安卓手机
- 界面与逻辑都刻意避开新语法/新 CSS：不用 Pointer Events（改用 `touchstart` + `mousedown`）、
  不用 flex/grid 的 `gap`、不用 `inset`、不用 CSS 变量以外的新特性 —— 老机型上自带的 WebView 也能正常显示与落子
- 不做鸿蒙原生包（按需求排除鸿蒙）；**鸿蒙 NEXT 不兼容 Android 应用**，装不了；
  鸿蒙 4 及更早版本理论上仍能安装 Android APK，但未做验证，请不要以此为准
- APK 用 **debug 证书**签名（自用没问题；若要上架应用商店需要换成你自己的正式签名）

---

## 2. 功能

与桌面版一致：

- **主页**：人机对战 / 人人对战（本地）/ **定式学习**
- **定式学习**：25 个常用开局（序号 + 名称 + 执子 + 难度，白 12 / 黑 13），棋盘上用箭头教你走变化，走完程序应一手并给出点评；有分支时会出现多个箭头让你选；卡片里可展开「开局讲解」（难度定位 / 典型计划 / 常见错误 / 主要变化）。内容与桌面版同源
- **难度与执子**：ELO 100–2000，每 100 分一档，共 20 档；档位**下方**可选「执白 · 先行 / 执黑 · 后行」
  （默认执白）。执黑时电脑先走，棋盘翻转成己方在下方
- **人人对战**：每走一步自动翻转棋盘，当前走棋方永远在下方
- **AI 解析**（人机对战，默认关闭）：白方胜率变化曲线、每一手的好坏符号
  （`!!` `!` `!?` `?!` `?` `??`，附英文与中文）、符号说明表。
  手机版一律**单列自上而下**排列，解析内容排在棋盘与走棋记录下方：

  ```
  走棋记录
  ──────────────
  AI 解析
    53%  白方胜率  +0.35     ← 胜率单独一行居中，在折线图上方
    全局胜率变化（分析中 n）
    [胜率折线图]
    每手评价（最新在下方）    ← 每一手的符号与损失分，列表自动滚到最新
    符号  English      中文    损失(兵)                  ← 符号说明表（优 → 劣，带表头）
    !!    Brilliant    精彩    唯一好棋
    ??    Blunder      大漏着  >3.00
  ```
- 困局判定完整：将杀、逼和、50 回合、三次重复局面、子力不足
- **显示控制格**（人机 / 人人都有，默认关闭）：一格被某方棋子攻击到就标一个角标 ——
  黑方标右上角黑三角、白方标左上角白三角（都带黑边），角与朝向按屏幕固定，不随棋盘翻转
- 悔棋、新局、升变选择（弹窗选后/车/象/马）
- 返回键：先关弹窗 → 再回主页 → 都没有才退出应用
- 对局中屏幕常亮；横竖屏都可玩（旋转不会丢棋局）

### 手机版与桌面版的两点差异

1. **分析强度略低**：手机端解析固定为深度 ≤ 5、每个局面 ≤ 0.35 秒（桌面版是 6 / 0.5 秒），
   避免在手机上卡顿。
2. **电脑思考是"同步"的**：WebView 里没有 Worker，搜索期间界面会短暂停住（低难度约 0.1–0.4 秒，
   最高难度约 1 秒），这段时间会先显示"电脑思考中…"。桌面版是后台线程，不受影响。

---

## 3. 目录结构

```
mobile/
├─ dist/
│   ├─ 国际象棋.apk          安卓安装包（构建产物）
│   └─ 国际象棋.html         单文件网页版（构建产物）
├─ web/                      游戏源码（三个文件，构建时内联成一个 HTML）
│   ├─ index.html            界面结构 + 样式
│   ├─ engine.js             规则与引擎（由桌面版 C# 逐段移植）
│   ├─ ui.js                 触屏交互、棋盘绘制、解析面板
│   ├─ pieces.png            棋子精灵图（由 tools/MakePieces.cs 生成）
│   └─ pieces.js             同一张图的 base64 内联版
├─ test/engine_test.js       Node 自检（perft / 一致性 / 解析 / 20 档难度）
├─ android/
│   ├─ app/src/main/         Android 工程（清单、Java、资源、图标）
│   │   ├─ java/com/dsh/chess/MainActivity.java   WebView 外壳
│   │   └─ res/              图标与主题
│   ├─ fetch_toolchain.ps1   下载 JDK / build-tools / platform（HTTP 镜像）
│   ├─ make_icons.py         生成各分辨率启动图标
│   ├─ pack_apk.py           把 dex 与网页资源打进 APK
│   └─ build_apk.ps1         **一键构建 APK**
├─ build_web.ps1             三个源文件 → 单文件 HTML
└─ shots/                    界面截图（构建时用于核对排版）

```

---

## 4. 自己重新构建

```powershell
# 1) 先下载工具链（约 300 MB，只需一次）
powershell -ExecutionPolicy Bypass -File mobile\android\fetch_toolchain.ps1

# 2) 生成单文件网页版 + APK
powershell -ExecutionPolicy Bypass -File mobile\android\build_apk.ps1

# 只想要网页版
powershell -ExecutionPolicy Bypass -File mobile\build_web.ps1
```

`build_apk.ps1` 做的事情（就是 Gradle 平时替我们做的事）：

```
aapt2 compile / link   → 带资源的未签名 APK（清单、resources.arsc、图标）
javac                  → Java 类（Java 8 字节码）
d8                     → classes.dex
pack_apk.py            → 放入 classes.dex 与 assets/index.html
zipalign + apksigner   → 对齐并用 debug 证书签名
```

**离线打包踩过的三个坑**（脚本里已处理，记下来免得重复踩）：

1. `aapt2` / `zipalign` 是原生程序，**处理不了含中文的绝对路径**（会报"系统找不到指定的文件"），
   所以脚本先复制到 `%TEMP%\chess-apk-build` 再打包；
2. 从 zip 解压出来的 SDK **没有 `find_java.bat`**，导致 `d8.bat` / `apksigner.bat` 检测不到 Java 后
   **静默什么都不做**（退出码还是 0），所以脚本直接调 `d8.jar` / `apksigner.jar`；
3. .NET 的 `ZipArchive` 重写 aapt2 生成的压缩包会把带 data descriptor 的条目写坏
   （`AndroidManifest.xml` 出现 size/crc32 mismatch），所以改用 Python 的 `zipfile` 重新打包。

---

## 5. 验证情况

| 检查 | 结果 |
| --- | --- |
| 引擎自检（Node，`mobile\test\engine_test.js`） | **435 项 0 失败**：6 个标准 perft 局面（起始局面深度 5 = 4,865,609 节点）、11,579 步随机对局的哈希/增量评估/悔棋一致性、SAN、AI 解析分级与胜率换算、20 档难度全部返回合法着法且不超时 |
| APK 结构 | `aapt2 dump badging` 通过：包名 `com.dsh.chess`、minSdk 21、targetSdk 29、名称「国际象棋」、5 档密度图标、启动 Activity 正确 |
| APK 签名 | `apksigner verify`：**v1 / v2 / v3 全部通过**，1 个签名者 |
| 包内容 | `AndroidManifest.xml`、`classes.dex`（dex 035）、`assets/index.html`（145 KB，引擎+精灵图+界面全部内联）、`resources.arsc`、5 张图标 |
| 界面核对 | `shots/` 下 6 张截图：主页、难度（20 档）、对局、AI 解析、横屏对局、横屏解析 |

> 说明：这台机器上没有安卓设备也没有模拟器，所以 **APK 没有在真机上实际运行过**；
> 上面的验证是"结构 + 签名 + 网页部分逻辑与渲染"三条线，其中网页部分与 APK 里跑的是同一份文件。
> 装到手机上如果遇到问题，把现象告诉我即可。

---

## 6. 已知限制

- 电脑思考期间界面会短暂停住（见上文"两点差异"），这是 WebView 单线程的限制
- 解析内容较多，需要向下滚动才能看完整（走棋记录 → 胜率 → 折线图 → 每手评价 → 符号表）
- 为保持"单列自上而下"的版式，横屏时也没有做"棋盘在左、面板在右"的分栏：
  棋盘按高度铺满，其余内容向下滚动查看
- APK 为 debug 签名；未做应用商店上架所需的 release 签名与混淆
- 未适配平板大屏的专门布局（会自动放大，属于"能用"级别）

---

## 7. 安全与兼容性核对（实测）

| 项目 | 结论 |
| --- | --- |
| 权限 | `aapt2 dump badging` 实测 **0 个权限**（连 INTERNET 都没有）→ 无法联网、读不到通讯录/存储 |
| WebView 加固 | 无 `addJavascriptInterface`（RCE 经典入口）；显式关闭 `setAllowFileAccessFromFileURLs` / `setAllowUniversalAccessFromFileURLs` / `setSaveFormData` / `setGeolocationEnabled`；关闭 devtools 调试；禁止 JS 自动开窗 |
| 清单 | `allowBackup=false`（阻止 adb backup 提取数据）、`usesCleartextTraffic=false`、`exported` 只给 LAUNCHER Activity 且不读 Intent 数据 |
| dex 内容 | 只有 6 个自有类（MainActivity + R），无 `http`/`Socket`/`Runtime.exec`/`ProcessBuilder`/JS 桥字符串 |
| 网页资源 | 无外部 URL、无 `eval`/`new Function`、无 `document.write`、无 XHR/fetch/WebSocket/localStorage/cookie → 完全自包含 |
| 兼容性加固 | 避开 Pointer Events（`touchstart`+`mousedown` 兜底）、flex/grid `gap`、`inset`、`grid` 布局；CSS 变量全部改成字面量颜色（`var()` 需要 Chrome 49+，老 WebView 会让整站配色失效） |
| 签名 | debug 证书固定放在 `android/debug.keystore`，重建后签名一致（可覆盖安装）；SHA-256 `939f1072…`；**不适合上架，需换正式签名** |
| 供应链 | JDK / build-tools / platform 与镜像公布的 SHA256、SHA1 校验一致；但校验和也是明文 HTTP 取的，无法排除镜像被同时篡改；最终 APK 的类与权限清单已逐项核对 |

**尚未验证的部分**：本机没有安卓设备也没有模拟器，**APK 没有真正安装运行过**；
验证到的是「APK 结构 + 签名（v1/v2/v3）+ 与 APK 内完全相同的网页在真实浏览器内核里的渲染与交互逻辑」。