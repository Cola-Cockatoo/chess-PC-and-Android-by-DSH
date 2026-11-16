# 国际象棋小游戏 · 构建脚本
#
# 说明：本机没有网络（pip / NuGet 不可用），因此不依赖 Python 打包器或 .NET SDK，
#       直接使用 Windows 自带的 .NET Framework C# 编译器 csc.exe 生成独立 exe。
#
# 用法：
#   powershell -ExecutionPolicy Bypass -File build.ps1              完整构建（含自检）
#   powershell -ExecutionPolicy Bypass -File build.ps1 -QuickTests  快速自检
#   powershell -ExecutionPolicy Bypass -File build.ps1 -SkipTests   跳过自检

param(
    [switch]$SkipTests,
    [switch]$QuickTests
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path

$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path $csc)) { $csc = "C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe" }
if (-not (Test-Path $csc)) { throw "找不到 C# 编译器 csc.exe（需要 .NET Framework 4.x）" }

$coreDir = Join-Path $root "src\Core"
$uiDir = Join-Path $root "src\UI"
$toolsDir = Join-Path $root "tools"
$distDir = Join-Path $root "dist"
New-Item -ItemType Directory -Force -Path $toolsDir, $distDir | Out-Null

$core = @(Get-ChildItem $coreDir -Filter *.cs | Select-Object -ExpandProperty FullName)
$ui = @(Get-ChildItem $uiDir -Filter *.cs | Select-Object -ExpandProperty FullName)
$program = @((Join-Path $root "src\Program.cs"), (Join-Path $root "src\AssemblyInfo.cs"))
$tests = Join-Path $root "tests\TestMain.cs"
if ($core.Count -eq 0 -or $ui.Count -eq 0) { throw "没有找到源文件" }

Write-Host "[1/3] 生成图标 ..."
$icon = Join-Path $distDir "chess.ico"
python (Join-Path $root "tools\make_icon.py") $icon | Out-Null
if (-not (Test-Path $icon)) { Write-Host "      图标生成失败，将使用默认图标" }

Write-Host "[2/3] 编译并运行自检程序 ..."
$testExe = Join-Path $toolsDir "chess_test.exe"
& $csc /nologo /target:exe /out:$testExe /codepage:65001 /optimize+ `
    /reference:System.dll,System.Core.dll ($core + $tests)
if ($LASTEXITCODE -ne 0) { throw "自检程序编译失败" }

if (-not $SkipTests) {
    $testArgs = @()
    if ($QuickTests) { $testArgs += "--quick" }
    & $testExe @testArgs
    if ($LASTEXITCODE -ne 0) { throw "自检未通过，已中止打包" }
}

Write-Host "[3/3] 编译主程序 ..."
$exe = Join-Path $distDir "国际象棋.exe"
$refs = "System.dll,System.Drawing.dll,System.Windows.Forms.dll,System.Core.dll"
$iconArg = @()
if (Test-Path $icon) { $iconArg = @("/win32icon:$icon") }
& $csc /nologo /target:winexe /out:$exe /codepage:65001 /optimize+ /platform:anycpu /win32manifest:src\app.manifest `
    @iconArg /reference:$refs ($core + $ui + $program)
if ($LASTEXITCODE -ne 0) { throw "主程序编译失败" }

Write-Host ""
Write-Host "构建完成：" -ForegroundColor Green
Write-Host "  可执行文件  $exe"
Write-Host "  自检程序    $testExe"
Write-Host "  图标        $icon"
