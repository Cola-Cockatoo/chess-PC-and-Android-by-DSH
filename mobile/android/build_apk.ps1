# 离线构建 Android 安装包（APK）
#
# 本机没有 Android Studio / Gradle，也不能联网解析依赖，所以这里用 Android SDK 里的
# 命令行工具手工走一遍 Gradle 的打包流程：
#
#   aapt2 compile/link  ->  带资源的未签名 APK（AndroidManifest.xml、resources.arsc、图标）
#   javac               ->  Java 类（Java 8 字节码，编译目标 android.jar）
#   d8                  ->  classes.dex
#   pack_apk.py         ->  把 classes.dex 与 assets/index.html 打进 APK
#   zipalign + apksigner->  对齐并用 debug 证书签名，得到可安装的 APK
#
# 两个必须绕开的坑（都写在这里以免以后再踩）：
#   1. aapt2 / zipalign 是原生工具，处理不了含中文的绝对路径 —— 所以先复制到 ASCII 临时目录再打包；
#   2. 解压出来的 SDK 里没有 find_java.bat，d8.bat / apksigner.bat 会静默什么都不做 —— 所以直接调 jar；
#      另外 .NET 的 ZipArchive 重写 aapt2 的压缩包会写坏条目，改用 Python 的 zipfile。
#
# 前置：先运行 mobile\android\fetch_toolchain.ps1 下载 JDK / build-tools / platform
# 用法： powershell -ExecutionPolicy Bypass -File build_apk.ps1

param(
    [string]$Toolchain = "$env:LOCALAPPDATA\dsh-android",
    [string]$OutDir = "",
    [string]$ApkName = "国际象棋",
    [switch]$SkipWeb
)

$ErrorActionPreference = "Stop"
$here = Split-Path -Parent $MyInvocation.MyCommand.Path           # mobile/android
$mobile = Split-Path -Parent $here                                # mobile

# 工具链不在默认位置时自动找一找（老版本下载到会话临时目录里）
if (-not (Test-Path (Join-Path $Toolchain "jdk\jdk-17\bin\javac.exe"))) {
    $cands = @("$env:TEMP\dsh-android", "$env:LOCALAPPDATA\dsh-android")
    $cands += (Get-ChildItem "$env:LOCALAPPDATA" -Directory -Filter "dsh-android" -ErrorAction SilentlyContinue | ForEach-Object { $_.FullName })
    foreach ($c in ($cands | Select-Object -Unique)) {
        if (Test-Path (Join-Path $c "jdk\jdk-17\bin\javac.exe")) { $Toolchain = $c; break }
    }
}
$jdk = Join-Path $Toolchain "jdk\jdk-17"
$bt = Join-Path $Toolchain "build-tools\android-10"
$androidJar = Join-Path $Toolchain "platform\android-10\android.jar"
$javaExe = Join-Path $jdk "bin\java.exe"
foreach ($p in @($jdk, $bt, $androidJar)) {
    if (-not (Test-Path $p)) { throw ("工具链缺失：" + $p + "，请先运行 fetch_toolchain.ps1") }
}
$env:JAVA_HOME = $jdk
$env:PATH = (Join-Path $jdk "bin") + ";" + $env:PATH

if (-not $OutDir) { $OutDir = Join-Path $mobile "dist" }
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

Write-Host "[1/7] 生成图标 ..."
python (Join-Path $here "make_icons.py") | Out-Null

if (-not $SkipWeb) {
    Write-Host "[2/7] 内联网页版（生成 assets/index.html）..."
    & powershell -NoLogo -ExecutionPolicy Bypass -File (Join-Path $mobile "build_web.ps1") | Out-Null
} else {
    Write-Host "[2/7] 跳过内联"
}

# ---- 复制到 ASCII 路径下打包（原生工具不认中文路径）----
$work = Join-Path $env:TEMP "chess-apk-build"
if (Test-Path $work) { Remove-Item $work -Recurse -Force }
New-Item -ItemType Directory -Force -Path $work | Out-Null
Copy-Item (Join-Path $here "app\src\main") (Join-Path $work "main") -Recurse -Force
$src = Join-Path $work "main"
$assetHtml = Join-Path $src "assets\index.html"
if (-not (Test-Path $assetHtml)) { throw "缺少 assets/index.html（先运行 build_web.ps1）" }

Write-Host "[3/7] aapt2 编译资源并链接 ..."
$aapt2 = Join-Path $bt "aapt2.exe"
& $aapt2 compile --dir (Join-Path $src "res") -o (Join-Path $work "res.zip")
if ($LASTEXITCODE -ne 0) { throw "aapt2 compile 失败" }

$unsigned = Join-Path $work "app-unsigned.apk"
& $aapt2 link -o $unsigned `
    -I $androidJar `
    --manifest (Join-Path $src "AndroidManifest.xml") `
    -R (Join-Path $work "res.zip") `
    --java (Join-Path $work "gen") `
    --min-sdk-version 21 --target-sdk-version 29 `
    --version-code 1 --version-name 1.0.0 `
    --auto-add-overlay
if ($LASTEXITCODE -ne 0) { throw "aapt2 link 失败" }

Write-Host "[4/7] javac 编译 Java（Java 8 字节码）..."
$javaFiles = @()
$javaFiles += (Get-ChildItem (Join-Path $src "java") -Recurse -Filter *.java | ForEach-Object { $_.FullName })
$javaFiles += (Get-ChildItem (Join-Path $work "gen") -Recurse -Filter *.java | ForEach-Object { $_.FullName })
New-Item -ItemType Directory -Force -Path (Join-Path $work "classes") | Out-Null
& (Join-Path $jdk "bin\javac.exe") -encoding UTF-8 -source 8 -target 8 -nowarn `
    -classpath $androidJar -d (Join-Path $work "classes") $javaFiles
if ($LASTEXITCODE -ne 0) { throw "javac 失败" }

Write-Host "[5/7] d8 生成 classes.dex ..."
$classFiles = (Get-ChildItem (Join-Path $work "classes") -Recurse -Filter *.class | ForEach-Object { $_.FullName })
New-Item -ItemType Directory -Force -Path (Join-Path $work "dex") | Out-Null
& $javaExe -Xmx1024M -cp (Join-Path $bt "lib\d8.jar") com.android.tools.r8.D8 `
    --min-api 21 --lib $androidJar --output (Join-Path $work "dex") $classFiles
if ($LASTEXITCODE -ne 0) { throw "d8 失败" }
$dex = Join-Path $work "dex\classes.dex"
if (-not (Test-Path $dex)) { throw "d8 没有产出 classes.dex" }

Write-Host "[6/7] 打包 dex 与 assets ..."
$packed = Join-Path $work "app-packed.apk"
python (Join-Path $here "pack_apk.py") $unsigned $dex (Split-Path -Parent $assetHtml) $packed
if ($LASTEXITCODE -ne 0) { throw "pack_apk.py 失败" }

Write-Host "[7/7] 对齐并签名 ..."
$aligned = Join-Path $work "app-aligned.apk"
& (Join-Path $bt "zipalign.exe") -f -p 4 $packed $aligned
if ($LASTEXITCODE -ne 0) { throw "zipalign 失败" }

$ks = Join-Path $here "debug.keystore"   # 固定位置：重建后签名一致，可直接覆盖安装
if (-not (Test-Path $ks)) {
    Write-Host "      生成 debug 签名证书 ..."
    & (Join-Path $jdk "bin\keytool.exe") -genkeypair -keystore $ks -storetype JKS `
        -storepass android -keypass android -alias androiddebugkey `
        -keyalg RSA -keysize 2048 -validity 10000 `
        -dname "CN=Android Debug,O=Android,C=CN" | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "keytool 失败" }
}

$signedTmp = Join-Path $work "app-signed.apk"
& $javaExe -jar (Join-Path $bt "lib\apksigner.jar") sign --ks $ks `
    --ks-pass pass:android --key-pass pass:android --ks-key-alias androiddebugkey `
    --v1-signing-enabled true --v2-signing-enabled true `
    --out $signedTmp $aligned
if ($LASTEXITCODE -ne 0) { throw "apksigner 失败" }

$apk = Join-Path $OutDir ($ApkName + ".apk")
Copy-Item $signedTmp $apk -Force

Write-Host ""
Write-Host "校验签名 ..." -ForegroundColor Cyan
& $javaExe -jar (Join-Path $bt "lib\apksigner.jar") verify --print-certs $signedTmp | Select-Object -First 4
Write-Host ""
Write-Host "APK 信息 ..." -ForegroundColor Cyan
& $aapt2 dump badging $signedTmp | Select-Object -First 8
Write-Host ""
Write-Host ("构建完成：" + $apk + "  " + [math]::Round((Get-Item $apk).Length / 1KB, 1) + " KB") -ForegroundColor Green