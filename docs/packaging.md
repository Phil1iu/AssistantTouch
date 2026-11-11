# 打包与安装（AssistantTouch）

本文档记录 Windows 发布包的构建、内容、安装/卸载、运行时要求与代码签名状态。

## 1. 构建与发布

```powershell
# 依赖：.NET 8 SDK（仓库 global.json 锁定 8.0.204，rollForward=latestFeature）
dotnet build AssistantTouch.sln -c Release
dotnet test  AssistantTouch.sln -c Release --no-build

# 框架依赖发布（需要目标机器安装 .NET 8 Desktop Runtime）
dotnet publish AssistantTouch.csproj -c Release -r win-x64 --self-contained false -o dist/publish/win-x64
```

- 发布方式：**framework-dependent**（体积小），目标机器需安装 **.NET 8 Desktop Runtime (x64)**：
  <https://dotnet.microsoft.com/download/dotnet/8.0>
- 如需免安装运行时，可改用 `--self-contained true`（体积显著变大，未在本轮产出）。

## 2. 发布包内容

`dist/AssistantTouch-win-x64.zip`（解压后即可运行/安装）：

| 文件 | 说明 |
| :--- | :--- |
| `AssistantTouch.exe` | 应用主程序（带应用图标，PerMonitorV2 manifest） |
| `AssistantTouch.dll` | 主程序集 |
| `AssistantTouch.deps.json` / `AssistantTouch.runtimeconfig.json` | .NET 依赖与运行时配置 |
| `README.md` | 功能与使用说明 |
| `packaging.md` | 本文件 |
| `install.ps1` / `uninstall.ps1` | 安装/卸载脚本 |

> `AssistantTouch.pdb` 仅用于调试，未放入 zip。

## 3. 安装

```powershell
# 在解压后的目录中运行
powershell -NoProfile -ExecutionPolicy Bypass -File install.ps1
# 可选：不创建桌面快捷方式
powershell -NoProfile -ExecutionPolicy Bypass -File install.ps1 -NoDesktopShortcut
```

- 安装目录：`%LOCALAPPDATA%\Programs\AssistantTouch`（当前用户，无需管理员）。
- 桌面快捷方式：`%USERPROFILE%\Desktop\AssistantTouch.lnk`，图标来自 exe 内嵌 `assets/app.ico`。
- 安装脚本**不写开机自启**；需要时启动应用后，在悬浮球菜单勾选「开机自启」
  （写入 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`，值名 `AssistantTouch`）。
- 安装前请先退出正在运行的应用（检测到运行中的 `AssistantTouch` 进程时，脚本会**中止**并提示 PID）。

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
- **.NET 8 Desktop Runtime (x64)**（framework-dependent 发布）。
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
- framework-dependent：目标机器必须装 .NET 8 Desktop Runtime。
- 混合 DPI / 触摸 / 托盘显隐等运行时行为仍需在真实桌面环境验证（当前构建环境锁屏）。
