# 打包与安装（AssistantTouch）

本文档记录 Windows 发布包的构建、内容、安装/卸载、运行时要求与代码签名状态。

## 1. 构建与发布

```powershell
# 依赖：.NET 8 SDK（仓库 global.json 锁定 8.0.204，rollForward=latestFeature）
dotnet build AssistantTouch.sln -c Release
dotnet test  AssistantTouch.sln -c Release --no-build

# 方案 A：框架依赖发布（需要目标机器安装 .NET 8 Desktop Runtime，体积小）
dotnet publish AssistantTouch.csproj -c Release -r win-x64 --self-contained false -o dist/publish/win-x64

# 方案 B：自包含发布（自带运行时，目标机器无需安装 .NET，体积大）
dotnet publish AssistantTouch.csproj -c Release -r win-x64 --self-contained true -o dist/publish/win-x64-sc
```

- 两种 payload 用**同一个** `install.ps1`：脚本复制发布目录的全部内容，不再依赖固定文件清单。
- 判断方式：目录里有 `hostfxr.dll` / `coreclr.dll` → self-contained；只有
  `AssistantTouch.runtimeconfig.json` 且引用 `Microsoft.WindowsDesktop.App` → framework-dependent。
- WPF 不支持裁剪（trimming），self-contained 包体积约 150 MB 属正常。

## 2. 发布包内容

`dist/AssistantTouch-win-x64.zip`（framework-dependent，解压后即可运行/安装）：

| 文件 | 说明 |
| :--- | :--- |
| `AssistantTouch.exe` | 应用主程序（带应用图标，PerMonitorV2 manifest） |
| `AssistantTouch.dll` | 主程序集 |
| `AssistantTouch.deps.json` / `AssistantTouch.runtimeconfig.json` | .NET 依赖与运行时配置 |
| `README.md` | 功能与使用说明 |
| `packaging.md` | 本文件 |
| `install.ps1` / `uninstall.ps1` | 安装/卸载脚本 |

`dist/AssistantTouch-win-x64-sc.zip`（self-contained）额外包含 `hostfxr.dll`、`coreclr.dll`、
`Microsoft.WindowsDesktop.App` 相关 DLL 以及本地化子目录（如 `zh-Hans\`）。打包时**整目录**入 zip，
并确保 `install.ps1`、`uninstall.ps1`、`README.md` 一并放在 zip 根目录（安装脚本会整体复制该目录）。

> `AssistantTouch.pdb` 仅用于调试，未放入 zip。

## 3. 安装

```powershell
# 在解压后的目录中运行
powershell -NoProfile -ExecutionPolicy Bypass -File install.ps1
# 可选：不创建桌面快捷方式
powershell -NoProfile -ExecutionPolicy Bypass -File install.ps1 -NoDesktopShortcut
```

- 安装目录：`%LOCALAPPDATA%\Programs\AssistantTouch`（当前用户，无需管理员）；卸载脚本对该目录整目录删除，对两种 payload 均有效。
- 桌面快捷方式：`%USERPROFILE%\Desktop\AssistantTouch.lnk`，图标来自 exe 内嵌 `assets/app.ico`。
- 安装脚本**不写开机自启**；需要时启动应用后，在悬浮球菜单勾选「开机自启」
  （写入 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`，值名 `AssistantTouch`）。
- 安装前请先退出正在运行的应用（检测到运行中的 `AssistantTouch` 进程时，脚本会**中止**并提示 PID）。
- 脚本把发布目录的**全部内容**复制到安装目录（含子目录），两种 payload 通用；在安装目录内重复运行会跳过复制。
- **运行时预检**：
  - framework-dependent：解析 `AssistantTouch.runtimeconfig.json`，若引用 `Microsoft.WindowsDesktop.App`，
    则检查 `%ProgramFiles%\dotnet\shared\Microsoft.WindowsDesktop.App\8.*`（PATH 上有 dotnet 时再用
    `dotnet --list-runtimes` 兜底）。缺失时**直接中止**并给下载链接，不再等到应用启动才报 `missing_runtime`。
  - self-contained：目录里有 `hostfxr.dll` / `coreclr.dll` 即跳过预检。

## 4. 卸载

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File uninstall.ps1
# 连同配置一起删除
powershell -NoProfile -ExecutionPolicy Bypass -File uninstall.ps1 -RemoveConfig
```

卸载会：

1. 结束正在运行的 `AssistantTouch` 进程；
2. 删除 HKCU Run 的 `AssistantTouch` 值（开机自启）；
3. 删除桌面快捷方式；
4. 删除安装目录 `%LOCALAPPDATA%\Programs\AssistantTouch`；
5. **默认保留**配置目录 `%LOCALAPPDATA%\AssistantTouch`（`window.json` 等），
   需要清理时加 `-RemoveConfig`（同时清理旧版本的 `%LOCALAPPDATA%\AssistiveTouch`）。

## 5. 运行时要求

- Windows 10 / 11（x64）。
- **framework-dependent 包**：必须安装 **.NET 8 Desktop Runtime (x64)**
  （<https://dotnet.microsoft.com/download/dotnet/8.0>）。`install.ps1` 会先预检，缺失即中止并给链接。
- **self-contained 包**：无需安装 .NET 运行时。
- 无需管理员权限；不写 HKLM。

## 6. 代码签名（阻塞项）

- 本项目**没有代码签名证书**，因此本轮发布包**未签名**。
- 未签名 exe 在首次运行时可能触发 Windows SmartScreen 提示；企业内部可通过可信来源分发缓解。
- 拿到证书后的签名命令模板：

```powershell
signtool sign /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 /f <证书.pfx> /p <证书密码> AssistantTouch.exe
```

> 状态：**阻塞（无证书）**。签名后再重新打 zip。

## 7. 验证清单

```powershell
# 1) 解压 zip 后检查文件齐全
# 2) 双击 AssistantTouch.exe 或从快捷方式启动
# 3) 悬浮球出现；右键/左键菜单、托盘图标、位置记忆、开机自启勾选正常
# 4) 卸载后桌面快捷方式消失、HKCU Run 值消失、配置默认保留
```

## 8. 已知限制

- 未签名（见第 6 节）。
- framework-dependent：目标机器必须装 .NET 8 Desktop Runtime；未装时 `install.ps1` 会中止（不会再出现安装成功但启动报 `missing_runtime`）。
- self-contained 包体积大（约 150 MB），分发时按需选择。
- 混合 DPI / 触摸 / 托盘显隐等运行时行为仍需在真实桌面环境验证（当前构建环境锁屏）。
