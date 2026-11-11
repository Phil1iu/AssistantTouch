# AssistiveTouch

Windows 桌面**半透明悬浮球**（骨架阶段）。

## 技术栈
- C# / WPF / .NET 8（`global.json` 已锁定 SDK 8.0.204）

## 当前功能
- 无边框透明窗口（`WindowStyle=None` + `AllowsTransparency=True`）
- 常驻最顶层（`Topmost=True`）、不在任务栏显示（`ShowInTaskbar=False`）
- 72×72 半透明白色圆球（`#66FFFFFF`）+ **2px 深色描边 `#99000000`** + 轻投影，悬停略微加深并放大 6%
  - 描边是白底可见性的关键：纯半透明填充在白底上对比仅约 8%，加描边后轮廓对比度 **7.00:1**（悬停 13.4~13.9:1，tester 像素实测；WCAG AA 正文 4.5:1 达标）。取证见 `artifacts/white-bg-probe.png`。
- 按住左键拖拽到屏幕任意位置（`DragMove()`）
- **右键菜单**（右键单击悬浮球，菜单显示在鼠标位置、压在球之上）
  - **重置位置**：回到主屏工作区中央，并立即写入位置文件
  - **关于**：显示名称、程序集版本与仓库地址
  - **退出**：弹确认框「退出 AssistiveTouch？」，**默认按钮为「否」**（直接回车不退出），选「是」才退出
  - 退出走 WPF 正常关闭 `Application.Current.Shutdown()`，不需要再 `taskkill`
- **位置记忆**：拖到哪，下次启动就回到哪
  - 存储：`%LOCALAPPDATA%\AssistiveTouch\window.json`，格式 `{"left": 924, "top": 480}`，单位 WPF DIU（不是物理像素）
  - 保存时机：拖拽结束、程序退出（临时文件 + 覆盖移动，写失败不影响运行）
  - 恢复时机：启动时读取；文件缺失、JSON 损坏、字段缺失或非数字时回落到屏幕居中，不崩溃
  - 越界坐标（例如保存过已断开显示器的位置）会按虚拟桌面把窗口完整钳回可见区域

## 构建与运行
```bash
dotnet build -c Release
./bin/Release/net8.0-windows/AssistiveTouch.exe
```

## 位置文件
- 路径：`%LOCALAPPDATA%\AssistiveTouch\window.json`（首次拖动或正常退出后生成）
- 内容：`{"left": <number>, "top": <number>}`，WPF DIU
- 删除该文件即恢复「首次运行」行为（屏幕居中）

## 已知问题 / 待决策
- **内存验收口径冲突**：本机空 WPF 模板空闲即 227.9MB（工作集），本项目 207~231MB，
  冲刺账本「空闲内存 < 50MB」在 C#/WPF + 本机环境下不可达。
  取证与建议见 [`artifacts/memory-probe.md`](artifacts/memory-probe.md)。

## 后续
- 单击/双击交互接入时需先处理与 `DragMove()` 的冲突（见 `MainWindow.xaml.cs` 注释）
