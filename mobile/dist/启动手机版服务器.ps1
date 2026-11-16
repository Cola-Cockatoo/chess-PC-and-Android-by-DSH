# 手机版局域网服务器（给 iPhone / iPad 用，不需要 Python、Node，也不需要管理员权限）
#
# 用法： 右键本文件 → "使用 PowerShell 运行"    （或在本目录执行下面的命令）
#         powershell -ExecutionPolicy Bypass -File .\启动手机版服务器.ps1
#
# 它会打印一个形如 http://192.168.1.5:8000/ 的网址：
#   1. 确保手机和这台电脑连的是同一个 Wi-Fi；
#   2. 手机 Safari 打开那个网址；
#   3. 分享 → 添加到主屏幕 → 添加，桌面就有「国际象棋」图标了。
#
# 关掉这个窗口 = 停止服务（图标还在，但那时打不开页面）。
# 首次运行 Windows 可能弹防火墙提示，勾选"专用网络"并允许访问。

param(
    [int]$Port = 8000,
    [string]$Root = $PSScriptRoot
)

$ErrorActionPreference = "Stop"
if (-not $Root) { $Root = (Get-Location).Path }

# 主页默认文件
$defaultFile = "国际象棋.html"
if (-not (Test-Path (Join-Path $Root $defaultFile))) {
    $alt = Get-ChildItem $Root -Filter *.html -File | Select-Object -First 1
    if ($alt) { $defaultFile = $alt.Name } else { throw "目录里没有 .html 文件：$Root" }
}

function Get-ContentType([string]$path) {
    switch ([System.IO.Path]::GetExtension($path).ToLower()) {
        ".html" { "text/html; charset=utf-8" }
        ".htm"  { "text/html; charset=utf-8" }
        ".png"  { "image/png" }
        ".jpg"  { "image/jpeg" }
        ".svg"  { "image/svg+xml" }
        ".css"  { "text/css; charset=utf-8" }
        ".js"   { "application/javascript; charset=utf-8" }
        ".json" { "application/json; charset=utf-8" }
        ".ico"  { "image/x-icon" }
        default { "application/octet-stream" }
    }
}

# 找出本机所有可用的 IPv4（排除回环与 169.254 自动地址）
# 注意：任何一处拿到单个字符串时都要用 @() 包成数组，否则 $ips[0] 取到的是第一个"字符"。
function Get-LanIps {
    $ips = @()
    try {
        $ips = @(Get-NetIPAddress -AddressFamily IPv4 -ErrorAction Stop |
            Where-Object { $_.IPAddress -notlike "127.*" -and $_.IPAddress -notlike "169.254.*" } |
            Select-Object -ExpandProperty IPAddress)
    } catch { }
    if ($ips.Count -eq 0) {
        try {
            $ips = @([System.Net.Dns]::GetHostAddresses([System.Net.Dns]::GetHostName()) |
                Where-Object { $_.AddressFamily -eq 'InterNetwork' } |
                ForEach-Object { $_.IPAddressToString })
        } catch { }
    }
    if ($ips.Count -eq 0) {
        # 最后一招：解析 ipconfig 的文字输出（中文系统显示为 "IPv4 地址 . . . : 192.168.1.5"）
        try {
            $ips = @(ipconfig | Select-String -Pattern "IPv4" | ForEach-Object {
                if ($_.Line -match "(\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3})") { $Matches[1] }
            })
        } catch { }
    }
    return @($ips | Where-Object { $_ -and $_ -notlike "127.*" -and $_ -notlike "169.254.*" } | Select-Object -Unique)
}

function Get-LanIp {
    $ips = @(Get-LanIps)
    if ($ips.Count -eq 0) { return "127.0.0.1" }
    # 优先常见的家用网段
    $pref = @($ips | Where-Object { $_ -like "192.168.*" -or $_ -like "10.*" -or $_ -match "^172\.(1[6-9]|2[0-9]|3[01])\." })
    if ($pref.Count -gt 0) { return $pref[0] }
    return $ips[0]
}

$listener = New-Object System.Net.Sockets.TcpListener([System.Net.IPAddress]::Any, $Port)
$listener.Start()
$ip = Get-LanIp
$allIps = @(Get-LanIps)
Write-Host ""
Write-Host "国际象棋 · 手机版服务器已启动" -ForegroundColor Green
Write-Host ("  目录： " + $Root)
Write-Host ("  手机 Safari 请打开：  http://" + $ip + ":" + $Port + "/") -ForegroundColor Cyan
if ($allIps.Count -gt 1) {
    Write-Host "  （若上面这个打不开，试试这几个地址：）" -ForegroundColor DarkGray
    foreach ($a in $allIps) { Write-Host ("      http://" + $a + ":" + $Port + "/") -ForegroundColor DarkGray }
}
Write-Host ("  本机自测：            http://localhost:" + $Port + "/")
Write-Host ""
Write-Host "  然后：分享 → 添加到主屏幕。按 Ctrl+C 停止。" -ForegroundColor Yellow
Write-Host ""

try {
    while ($true) {
        $client = $listener.AcceptTcpClient()
        try {
            $client.ReceiveTimeout = 5000
            $client.SendTimeout = 10000
            $stream = $client.GetStream()
            $reader = New-Object System.IO.StreamReader($stream, [System.Text.Encoding]::ASCII, $false, 1024, $true)
            $requestLine = $reader.ReadLine()
            if (-not $requestLine) { continue }

            # 把请求头读完（我们不看内容，但必须读完，否则浏览器会卡住）
            while ($true) {
                $line = $reader.ReadLine()
                if ($null -eq $line -or $line -eq "") { break }
            }

            $parts = $requestLine.Split(" ")
            $rawPath = if ($parts.Length -ge 2) { $parts[1] } else { "/" }
            $path = [System.Uri]::UnescapeDataString(($rawPath -split "\?")[0])
            if ($path -eq "/" -or $path -eq "") { $path = "/" + $defaultFile }
            $rel = $path.TrimStart("/").Replace("/", [System.IO.Path]::DirectorySeparatorChar)
            $full = Join-Path $Root $rel

            # 防目录穿越：解析后的路径必须仍在 Root 之内
            $rootFull = [System.IO.Path]::GetFullPath($Root)
            $fileFull = [System.IO.Path]::GetFullPath($full)
            $ok = $fileFull.StartsWith($rootFull, [System.StringComparison]::OrdinalIgnoreCase)

            if ($ok -and (Test-Path $fileFull -PathType Leaf)) {
                $bytes = [System.IO.File]::ReadAllBytes($fileFull)
                $head = "HTTP/1.1 200 OK`r`n" +
                        "Content-Type: " + (Get-ContentType $fileFull) + "`r`n" +
                        "Content-Length: " + $bytes.Length + "`r`n" +
                        "Cache-Control: no-store`r`n" +
                        "Connection: close`r`n`r`n"
                $headBytes = [System.Text.Encoding]::ASCII.GetBytes($head)
                $stream.Write($headBytes, 0, $headBytes.Length)
                $stream.Write($bytes, 0, $bytes.Length)
                Write-Host ("  200  " + $rel + "  (" + [math]::Round($bytes.Length / 1KB, 1) + " KB)")
            } else {
                $body = [System.Text.Encoding]::UTF8.GetBytes("404 Not Found")
                $head = "HTTP/1.1 404 Not Found`r`nContent-Type: text/plain; charset=utf-8`r`n" +
                        "Content-Length: " + $body.Length + "`r`nConnection: close`r`n`r`n"
                $headBytes = [System.Text.Encoding]::ASCII.GetBytes($head)
                $stream.Write($headBytes, 0, $headBytes.Length)
                $stream.Write($body, 0, $body.Length)
                Write-Host ("  404  " + $rawPath) -ForegroundColor DarkYellow
            }
            $stream.Flush()
        } catch {
            Write-Host ("  请求出错：" + $_.Exception.Message) -ForegroundColor DarkYellow
        } finally {
            $client.Close()
        }
    }
} finally {
    $listener.Stop()
    Write-Host "服务器已停止"
}
