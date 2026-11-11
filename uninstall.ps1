# 卸载 AssistantTouch：停止进程、删除 HKCU Run 自启项、桌面快捷方式与安装目录。
# 用法：powershell -NoProfile -ExecutionPolicy Bypass -File uninstall.ps1
# 默认保留用户配置（%LOCALAPPDATA%\AssistantTouch）；加 -RemoveConfig 一并删除（含旧版 %LOCALAPPDATA%\AssistiveTouch）。
param(
    [switch]$RemoveConfig
)

$ErrorActionPreference = "Stop"
$appName = "AssistantTouch"
$installDir = Join-Path $env:LOCALAPPDATA "Programs\$appName"
$configDir = Join-Path $env:LOCALAPPDATA $appName

Get-Process $appName -ErrorAction SilentlyContinue | Stop-Process -Force

$runKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run"
if (Test-Path $runKey) {
    Remove-ItemProperty -Path $runKey -Name $appName -ErrorAction SilentlyContinue
}
Write-Host "已移除开机自启项（如存在）"

$shortcutPath = Join-Path ([Environment]::GetFolderPath("Desktop")) "$appName.lnk"
if (Test-Path $shortcutPath) {
    Remove-Item -Path $shortcutPath -Force
    Write-Host "已删除桌面快捷方式"
}

if (Test-Path $installDir) {
    Remove-Item -Path $installDir -Recurse -Force
    Write-Host "已删除安装目录: $installDir"
}

if ($RemoveConfig) {
    $legacyConfigDir = Join-Path $env:LOCALAPPDATA "AssistiveTouch"
    foreach ($dir in @($configDir, $legacyConfigDir)) {
        if (Test-Path $dir) {
            Remove-Item -Path $dir -Recurse -Force
            Write-Host "已删除配置目录: $dir"
        }
    }
}
else {
    Write-Host "已保留配置目录: $configDir（如需删除，重新运行并加 -RemoveConfig）"
}
