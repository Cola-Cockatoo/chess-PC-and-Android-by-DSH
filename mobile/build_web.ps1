# 把网页版几个文件（pieces.js / engine.js / openings.js / review.js / ui.js / study.js）内联成单个 HTML
#
# 产出：
#   mobile/dist/国际象棋.html            单文件网页版（手机浏览器直接打开、可"添加到主屏幕"）
#   mobile/dist/apple-touch-icon.png     iOS 添加到主屏幕时用的图标
#   mobile/android/app/src/main/assets/index.html   给 APK 用的资源
#
# 用法： powershell -ExecutionPolicy Bypass -File build_web.ps1

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path          # mobile/
$web = Join-Path $root "web"
$outDir = Join-Path $root "dist"
$assets = Join-Path $root "android\app\src\main\assets"
$iosWww = Join-Path $root "ios\ChessIOS\www"
New-Item -ItemType Directory -Force -Path $outDir, $assets, $iosWww | Out-Null

# iOS 主屏图标（存在就一起分发；HTML 里用相对路径引用）
$icon = Join-Path $web "apple-touch-icon.png"
if (-not (Test-Path $icon)) {
    & python (Join-Path $root "make_ios_icon.py") | Out-Null
}
if (Test-Path $icon) {
    Copy-Item $icon (Join-Path $outDir "apple-touch-icon.png") -Force
    Copy-Item $icon (Join-Path $assets "apple-touch-icon.png") -Force
    Copy-Item $icon (Join-Path $iosWww "apple-touch-icon.png") -Force
}

$html = [System.IO.File]::ReadAllText((Join-Path $web "index.html"), [System.Text.Encoding]::UTF8)
foreach ($name in @("pieces.js", "engine.js", "openings.js", "review.js", "ui.js", "study.js")) {
    $tag = '<script src="' + $name + '"></script>'
    if ($html.IndexOf($tag) -lt 0) { throw ("index.html 里找不到 " + $tag) }
    $code = [System.IO.File]::ReadAllText((Join-Path $web $name), [System.Text.Encoding]::UTF8)
    if ($code.IndexOf("</script>") -ge 0) { throw ($name + " 里含 </script>，不能直接内联") }
    $html = $html.Replace($tag, "<script>`n" + $code + "`n</script>")
}

$single = Join-Path $outDir "国际象棋.html"
[System.IO.File]::WriteAllText($single, $html, (New-Object System.Text.UTF8Encoding($false)))
[System.IO.File]::WriteAllText((Join-Path $assets "index.html"), $html, (New-Object System.Text.UTF8Encoding($false)))
# iOS 工程：同一份 HTML 直接作为 App 内页面
[System.IO.File]::WriteAllText((Join-Path $iosWww "index.html"), $html, (New-Object System.Text.UTF8Encoding($false)))

Write-Host ("单文件网页版: " + $single + "  " + [math]::Round((Get-Item $single).Length / 1KB, 1) + " KB")
Write-Host ("iOS 图标:     " + (Join-Path $outDir "apple-touch-icon.png"))
Write-Host ("APK 资源:     " + (Join-Path $assets "index.html"))
Write-Host ("iOS 页面:     " + (Join-Path $iosWww "index.html"))
