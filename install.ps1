# 安装 AssistantTouch：复制到 %LOCALAPPDATA%\Programs\AssistantTouch 并创建桌面快捷方式。
# 用法：在解压后的发布目录里运行
#   powershell -NoProfile -ExecutionPolicy Bypass -File install.ps1
# 可选：-NoDesktopShortcut
param(
    [switch]$NoDesktopShortcut
)

$ErrorActionPreference = "Stop"
$appName = "AssistantTouch"
$sourceDir = $PSScriptRoot
$installDir = Join-Path $env:LOCALAPPDATA "Programs\$appName"
$exeSource = Join-Path $sourceDir "$appName.exe"

if (-not (Test-Path $exeSource)) {
    throw "找不到 $exeSource。请在解压后的 AssistantTouch 发布目录中运行本脚本。"
}

$running = Get-Process $appName -ErrorAction SilentlyContinue
if ($running) {
    Write-Warning "检测到 $appName 正在运行。请先从托盘菜单退出，再重新运行安装脚本。"
}

New-Item -ItemType Directory -Path $installDir -Force | Out-Null

$payload = @(
    "$appName.exe",
    "$appName.dll",
    "$appName.deps.json",
    "$appName.runtimeconfig.json",
    "README.md",
    "packaging.md",
    "install.ps1",
    "uninstall.ps1"
)

foreach ($name in $payload) {
    $source = Join-Path $sourceDir $name
    if (Test-Path $source) {
        Copy-Item -Path $source -Destination (Join-Path $installDir $name) -Force
    }
}

if (-not $NoDesktopShortcut) {
    $desktop = [Environment]::GetFolderPath("Desktop")
    $shortcutPath = Join-Path $desktop "$appName.lnk"
    $exeTarget = Join-Path $installDir "$appName.exe"

    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($shortcutPath)
    $shortcut.TargetPath = $exeTarget
    $shortcut.WorkingDirectory = $installDir
    $shortcut.IconLocation = "$exeTarget,0"
    $shortcut.Description = "AssistantTouch 悬浮球"
    $shortcut.Save()

    Write-Host "已创建桌面快捷方式: $shortcutPath"
}

Write-Host ""
Write-Host "已安装到: $installDir"
Write-Host "启动:     $installDir\$appName.exe"
Write-Host "卸载:     powershell -NoProfile -File `"$installDir\uninstall.ps1`""
Write-Host ""
Write-Host "说明：安装脚本不写开机自启；如需自启，启动后在悬浮球菜单里勾选「开机自启」。"
