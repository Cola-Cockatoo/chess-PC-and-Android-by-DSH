# 下载构建 Android 安装包所需的工具链（本机 HTTPS 被重置，只能用 HTTP 镜像）
#
# 本机没有 JDK、没有 Android SDK、也没有 Gradle（且 Gradle 在线下载也不可能），
# 所以从国内 HTTP 镜像拉取「OpenJDK 17 + Android build-tools + Android platform」，
# 再用 aapt2 / d8 / apksigner 手工打包 APK（等价于 Gradle assembleDebug 做的事）。
#
# 用法： powershell -ExecutionPolicy Bypass -File fetch_toolchain.ps1
# 特性：断点续传 + 重试；用"能否打开 zip"来校验完整性（不依赖文件大小）

param(
    # 默认放在固定的用户目录：会话临时目录（%TEMP%\dsh-xxxx）会被系统清理，工具链放那里会丢
    [string]$Root = "$env:LOCALAPPDATA\dsh-android"
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.IO.Compression.FileSystem
$mirror = "http://mirrors.huaweicloud.com/android/repository"
$files = @(
    @{ Url = "http://repo.huaweicloud.com/openjdk/17/openjdk-17_windows-x64_bin.zip"; Name = "jdk.zip" },
    @{ Url = "$mirror/build-tools_r29.0.3-windows.zip"; Name = "build-tools.zip" },
    @{ Url = "$mirror/platform-29_r04.zip";             Name = "platform.zip" }
)

New-Item -ItemType Directory -Force -Path $Root | Out-Null

function Test-Zip([string]$path) {
    if (-not (Test-Path $path)) { return $false }
    try {
        $z = [System.IO.Compression.ZipFile]::OpenRead($path)
        $n = $z.Entries.Count
        $z.Dispose()
        return $n -gt 0
    } catch {
        return $false
    }
}

foreach ($f in $files) {
    $out = Join-Path $Root $f.Name
    for ($attempt = 1; $attempt -le 8; $attempt++) {
        if (Test-Zip $out) { break }
        $have = if (Test-Path $out) { [math]::Round((Get-Item $out).Length / 1MB, 1) } else { 0 }
        Write-Host ("[{0}] 下载 {1}（已有 {2} MB）" -f $attempt, $f.Name, $have)
        & curl.exe -L -C - --fail --retry 4 --retry-delay 3 --retry-all-errors --no-progress-meter --max-time 900 -o $out $f.Url
        Start-Sleep -Seconds 2
    }
    if (-not (Test-Zip $out)) { throw ("压缩包不完整或下载失败：" + $f.Name) }
    Write-Host ("  完成 " + $f.Name + "  " + [math]::Round((Get-Item $out).Length / 1MB, 1) + " MB")
}

Write-Host "解压 ..."
foreach ($f in $files) {
    $zip = Join-Path $Root $f.Name
    $dir = Join-Path $Root ($f.Name -replace '\.zip$', '')
    $marker = Join-Path $dir ".extracted"
    if (Test-Path $marker) { Write-Host "已解压，跳过：$dir"; continue }
    if (Test-Path $dir) { Remove-Item $dir -Recurse -Force }
    Expand-Archive -Path $zip -DestinationPath $dir -Force
    New-Item -ItemType File -Path $marker -Force | Out-Null
    Write-Host "  解压完成 $dir"
}

Write-Host ""
Write-Host "工具链就绪：" -ForegroundColor Green
Get-ChildItem $Root -Directory | ForEach-Object { Write-Host ("  " + $_.FullName) }
