# 安装 AssistantTouch：把发布目录整体复制到 %LOCALAPPDATA%\Programs\AssistantTouch 并创建桌面快捷方式。
# 支持两种 payload：
#   - framework-dependent（有 AssistantTouch.runtimeconfig.json，依赖 .NET 8 Desktop Runtime）
#   - self-contained（有 hostfxr.dll / coreclr.dll，免运行时）
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
    throw "检测到 $appName 正在运行（PID: $($running.Id -join ', ')）。请先从托盘菜单退出，再重新运行安装脚本。"
}

# ---- 运行时预检 ----------------------------------------------------------
# self-contained 包自带 hostfxr/coreclr，不需要目标机器装运行时；
# framework-dependent 包缺运行时会表现为启动即退（missing_runtime），这里提前拦住。
function Test-SelfContainedPayload {
    return (Test-Path (Join-Path $sourceDir "hostfxr.dll")) -or (Test-Path (Join-Path $sourceDir "coreclr.dll"))
}

function Get-DesktopRuntimeMissingReason {
    # 返回 $null 表示无需预检或 8.x Desktop Runtime 可用；否则返回缺失说明。
    $runtimeConfigPath = Join-Path $sourceDir "$appName.runtimeconfig.json"
    if (-not (Test-Path $runtimeConfigPath)) {
        return $null
    }

    try {
        $config = Get-Content -Raw $runtimeConfigPath | ConvertFrom-Json
    }
    catch {
        Write-Warning "无法解析 $runtimeConfigPath，跳过运行时预检。"
        return $null
    }

    $frameworks = @()
    if ($null -ne $config.runtimeOptions.framework) {
        $frameworks += $config.runtimeOptions.framework
    }
    if ($null -ne $config.runtimeOptions.frameworks) {
        $frameworks += $config.runtimeOptions.frameworks
    }

    $needsDesktopRuntime = $false
    foreach ($framework in $frameworks) {
        if ($framework.name -eq "Microsoft.WindowsDesktop.App") {
            $needsDesktopRuntime = $true
        }
    }
    if (-not $needsDesktopRuntime) {
        return $null
    }

    $sharedRoot = Join-Path $env:ProgramFiles "dotnet\shared\Microsoft.WindowsDesktop.App"
    if (Test-Path $sharedRoot) {
        $found = Get-ChildItem -Path $sharedRoot -Directory -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -like "8.*" } |
            Select-Object -First 1
        if ($null -ne $found) {
            return $null
        }
    }

    # dotnet 可能不在 Program Files：在 PATH 上时用 --list-runtimes 兜底。
    $dotnet = Get-Command dotnet.exe -ErrorAction SilentlyContinue
    if ($null -ne $dotnet) {
        try {
            $runtimes = & $dotnet.Source --list-runtimes 2>$null
            if ($runtimes | Where-Object { $_ -like "Microsoft.WindowsDesktop.App 8.*" } | Select-Object -First 1) {
                return $null
            }
        }
        catch {
            # 查询失败按缺失处理。
        }
    }

    return ("缺少 .NET 8 Desktop Runtime (x64)：未找到 `"$sharedRoot\8.*`"。`n" +
        "请先安装 https://dotnet.microsoft.com/download/dotnet/8.0 里的 Desktop Runtime（不是 Console Runtime），再重新运行本脚本。")
}

if (Test-SelfContainedPayload) {
    Write-Host "运行时预检: self-contained 包（自带 hostfxr/coreclr），无需安装 .NET 运行时。"
}
else {
    $missingReason = Get-DesktopRuntimeMissingReason
    if ($null -ne $missingReason) {
        throw $missingReason
    }
    Write-Host "运行时预检: 已找到 .NET 8 Desktop Runtime。"
}

# ---- 复制 payload --------------------------------------------------------
New-Item -ItemType Directory -Path $installDir -Force | Out-Null

$sourceFull = (Resolve-Path -Path $sourceDir).Path.TrimEnd('\')
$installFull = (Resolve-Path -Path $installDir).Path.TrimEnd('\')
$sourceKey = $sourceFull.ToLowerInvariant()
$installKey = $installFull.ToLowerInvariant()

if ($sourceKey -eq $installKey -or $sourceKey.StartsWith($installKey + '\')) {
    Write-Host "已在安装目录中运行，跳过复制: $installFull"
}
elseif ($installKey.StartsWith($sourceKey + '\')) {
    throw "安装目录（$installFull）位于发布目录内部，无法安装。请把发布包解压到其它位置后运行。"
}
else {
    # 覆盖安装时先清空应用目录，避免上一种 payload 的残留（如 SC 包的 hostfxr.dll）混进来。
    Get-ChildItem -Path $installFull -Force | Remove-Item -Recurse -Force

    # 整目录复制：framework-dependent 与 self-contained 的 payload 文件集合不同，
    # 不再维护固定文件清单；子目录（如 self-contained 的本地化资源）一并复制。
    Get-ChildItem -Path $sourceFull -Force | ForEach-Object {
        Copy-Item -Path $_.FullName -Destination $installFull -Recurse -Force
    }
    Write-Host "已复制发布目录内容: $sourceFull -> $installFull"
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
