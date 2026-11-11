# AssistantTouch

Windows 桌面**极简白色悬浮球**（骨架阶段）。

## 技术栈
- C# / WPF / .NET 8（`global.json` 已锁定 SDK 8.0.204）

## 当前功能
- 无边框透明窗口（`WindowStyle=None` + `AllowsTransparency=True`）
- 常驻最顶层（`Topmost=True`）、不在任务栏显示（`ShowInTaskbar=False`）
- 72×72 **近纯白圆球 + 1px 细灰渐变环**（风格 B，Issue #1）
  - 填充：常态 `#F5FFFFFF`（约 96% 白），悬停 `#FFFFFFFF`；保留轻微半透明观感
  - 环：`StrokeThickness=1`，对角 `LinearGradientBrush`（`0.146,0.146 → 0.854,0.854`，端点收在椭圆环上）；常态 `#80000000 → #1F000000`，悬停 `#A6000000 → #40000000`
  - 无投影（已删除 `DropShadowEffect`）、无 2px 深色粗描边；悬停缩放 1.04
  - 白底可访问性口径（WCAG 非文本）：环最深像素常态 **≥3:1**、悬停 **≥4.5:1**；具体比值**待 tester 像素实测**（静态阶段不写估算值）
- **左键交互**（手动拖拽，4px 阈值；不再使用 `DragMove()`）
  - 拖动超过 4 设备像素 → 移动球；拖动中持续钳制在最近显示器工作区内，松手不回弹
  - 位移未超过阈值 → 视为单击，延迟到系统双击时间（`GetDoubleClickTime`，钳制 200–1000ms）后打开菜单
  - 双击 → 回到主屏工作区中央并立即保存位置
- **触摸**：与鼠标共用同一套状态机（4px 阈值、连续钳制、无回弹）
  - 单指拖动 → 移动球；轻触 → 系统双击时间后打开菜单；双击 → 回到主屏工作区中央
  - 多指时只由第一个触点驱动，额外触点忽略
  - 当前环境**无触摸屏，未实机验证**
- **右键菜单**（右键单击悬浮球，或左键单击；菜单显示在鼠标位置、压在球之上）
  - 浅色主题：白底、1px 灰边、6px 圆角、悬停 `#F2F2F2`、分隔线 `#E5E5E5`、文字 `#1A1A1A`
  - **重置位置**：回到主屏工作区中央，并立即写入位置文件
  - **关于**：显示名称、构建版本（优先取带 `+<commit>` 戳的 InformationalVersion）与仓库地址
  - **退出**：弹确认框「退出 AssistantTouch？」，**默认按钮为「否」**（直接回车不退出），选「是」才退出
  - 退出走 WPF 正常关闭 `Application.Current.Shutdown()`，不需要再 `taskkill`
- **托盘图标**（任务栏通知区）：白色圆球图标；双击切换悬浮球显示/隐藏
  - 托盘菜单：显示/隐藏悬浮球、重置位置、关于、退出
- **开机自启**：右键菜单「开机自启」可勾选/取消；写入 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`，值名 `AssistantTouch`，值为带引号的 exe 完整路径；只影响当前用户，不碰 HKLM
- **单实例**：同一 Windows 用户（**跨登录会话**）只允许一个进程；互斥量 `Global\AssistantTouch.SingleInstance.<SID>`，权限不足时回退 `Local\AssistantTouch.SingleInstance.<SID>`；第二次启动直接退出，不创建窗口、不读写位置文件。不同用户可各运行一个实例。
- **位置记忆**：拖到哪，下次启动就回到哪
  - 存储：`%LOCALAPPDATA%\AssistantTouch\window.json`，格式 `{"left": 924, "top": 480}`，单位 WPF DIU（不是物理像素）
  - 保存时机：拖拽结束、程序退出（临时文件名含进程 ID，写完再覆盖移动；写失败不影响运行）
  - 恢复时机：启动时读取；文件缺失、JSON 损坏、字段缺失或非数字时回落到屏幕居中，不崩溃
  - 越界/跨屏坐标会按**窗口中心点所在（或最近）显示器的工作区**钳制（`MonitorFromPoint` + `GetMonitorInfo`）；拖拽结束也会再钳一次，球不会被拖丢。双屏错位死区由「最近显示器」兜底

## 构建与运行
```bash
dotnet build -c Release
./bin/Release/net8.0-windows/AssistantTouch.exe
```

## 测试与打包
```powershell
dotnet test AssistantTouch.sln -c Release
dotnet publish AssistantTouch.csproj -c Release -r win-x64 --self-contained false -o dist/publish/win-x64
```

- 打包、安装/卸载脚本、运行时要求与签名状态见 [`docs/packaging.md`](docs/packaging.md)
  （当前**无代码签名证书**，签名列为阻塞项）。

## 位置文件
- 路径：`%LOCALAPPDATA%\AssistantTouch\window.json`（首次拖动或正常退出后生成）
- 内容：`{"left": <number>, "top": <number>}`，WPF DIU
- 删除该文件即恢复「首次运行」行为（屏幕居中）
- **从旧版本（`AssistiveTouch`）升级**：若新路径不存在、旧路径 `%LOCALAPPDATA%\AssistiveTouch\window.json` 存在且合法，首次启动会读取一次并写入新路径（不删除旧文件）；之后只读写新路径

## 已知问题 / 待决策
- **内存验收口径冲突**：本机空 WPF 模板空闲即 227.9MB（工作集），本项目 207~231MB，
  冲刺账本「空闲内存 < 50MB」在 C#/WPF + 本机环境下不可达。
  取证与建议见 [`artifacts/memory-probe.md`](artifacts/memory-probe.md)。
- **PerMonitorV2（高 DPI）**：`app.manifest` 声明 PerMonitorV2，旧系统回落 `true/PM`；保存的位置按窗口所在显示器的 DPI 折算；钳制时按**目标显示器 DPI**（`GetDpiForMonitor`）折算工作区；跨显示器/DPI 变化通过 `DpiChanged` 重新钳制，拖拽中会重置指针与窗口基准避免跳变。混合 DPI 下的实际表现**未实机验证**（当前环境为 100%）。
- **单实例范围**：`Global\AssistantTouch.SingleInstance.<SID>` 按 Windows 用户隔离（同一用户跨会话互斥、不同用户各一个）；创建失败回退 `Local\...<SID>`；都失败则按「已有实例」退出。未做不同用户之间的互斥。

## 后续
- 单击/双击交互接入时需先处理与 `DragMove()` 的冲突（见 `MainWindow.xaml.cs` 注释）
