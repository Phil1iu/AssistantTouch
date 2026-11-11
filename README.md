# AssistantTouch

Windows 桌面**极简白色悬浮球**（骨架阶段）。

## 技术栈
- C# / WPF / .NET 8（`global.json` 已锁定 SDK 8.0.204）

## 当前功能
- 无边框透明窗口（`WindowStyle=None` + `AllowsTransparency=True`）
- 常驻最顶层（`Topmost=True`）、不在任务栏显示（`ShowInTaskbar=False`）
- 72×72 **近纯白圆球 + 1px 细灰渐变环**（风格 B，Issue #1）
  - 填充：常态 `#F5FFFFFF`（约 96% 白），悬停 `#FFFFFFFF`；保留轻微半透明观感
  - 环：`StrokeThickness=1`，对角 `LinearGradientBrush`（`0,0 → 1,1`）；常态 `#73000000 → #1F000000`，悬停 `#A6000000 → #40000000`
  - 无投影（已删除 `DropShadowEffect`）、无 2px 深色粗描边；悬停缩放 1.04
  - 白底可访问性口径（WCAG 非文本）：环最深像素常态 **≥3:1**、悬停 **≥4.5:1**。按标称色值对纯白底估算：常态约 **3.36:1**、悬停约 **7.00:1**（估算值，未计抗锯齿；以 tester 像素实测为准）
- 按住左键拖拽到屏幕任意位置（`DragMove()`）
- **右键菜单**（右键单击悬浮球，菜单显示在鼠标位置、压在球之上）
  - **重置位置**：回到主屏工作区中央，并立即写入位置文件
  - **关于**：显示名称、构建版本（优先取带 `+<commit>` 戳的 InformationalVersion）与仓库地址
  - **退出**：弹确认框「退出 AssistantTouch？」，**默认按钮为「否」**（直接回车不退出），选「是」才退出
  - 退出走 WPF 正常关闭 `Application.Current.Shutdown()`，不需要再 `taskkill`
- **位置记忆**：拖到哪，下次启动就回到哪
  - 存储：`%LOCALAPPDATA%\AssistantTouch\window.json`，格式 `{"left": 924, "top": 480}`，单位 WPF DIU（不是物理像素）
  - 保存时机：拖拽结束、程序退出（临时文件名含进程 ID，写完再覆盖移动；写失败不影响运行）
  - 恢复时机：启动时读取；文件缺失、JSON 损坏、字段缺失或非数字时回落到屏幕居中，不崩溃
  - 越界坐标（例如保存过已断开显示器的位置）会按**虚拟桌面矩形**钳制。该矩形是所有显示器的**外接矩形**，不等于各显示器的并集：多显示器错位时，矩形角落可能没有物理屏覆盖（已知限制，详见下节）

## 构建与运行
```bash
dotnet build -c Release
./bin/Release/net8.0-windows/AssistantTouch.exe
```

## 位置文件
- 路径：`%LOCALAPPDATA%\AssistantTouch\window.json`（首次拖动或正常退出后生成）
- 内容：`{"left": <number>, "top": <number>}`，WPF DIU
- 删除该文件即恢复「首次运行」行为（屏幕居中）
- **从旧版本（`AssistiveTouch`）升级**：若新路径不存在、旧路径 `%LOCALAPPDATA%\AssistiveTouch\window.json` 存在且合法，首次启动会读取一次并写入新路径（不删除旧文件）；之后只读写新路径

## 已知问题 / 待决策
- **内存验收口径冲突**：本机空 WPF 模板空闲即 227.9MB（工作集），本项目 207~231MB，
  冲刺账本「空闲内存 < 50MB」在 C#/WPF + 本机环境下不可达。
  取证与建议见 [`artifacts/memory-probe.md`](artifacts/memory-probe.md)。
- **多显示器错位钳制（P2，已知限制）**：越界钳制用虚拟桌面外接矩形，矩形角落可能不属于任何显示器（本机副屏 `y=-51` 时实测 `(0,-51)` 仅 21px 落在物理屏）。未做按最近显示器工作区的钳制。
- **高 DPI 未适配**：保存的是 WPF DIU；125%/150% 缩放、跨不同 DPI 显示器迁移位置未验证。
- **多实例位置文件**：临时文件已按进程 ID 区分；若同时运行多个实例，最后保存者覆盖位置（未做单实例互斥）。

## 后续
- 单击/双击交互接入时需先处理与 `DragMove()` 的冲突（见 `MainWindow.xaml.cs` 注释）
