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
- 启动位置：屏幕居中

## 构建与运行
```bash
dotnet build -c Release
./bin/Release/net8.0-windows/AssistiveTouch.exe
```

## 已知问题 / 待决策
- **内存验收口径冲突**：本机空 WPF 模板空闲即 227.9MB（工作集），本项目 207~231MB，
  冲刺账本「空闲内存 < 50MB」在 C#/WPF + 本机环境下不可达。
  取证与建议见 [`artifacts/memory-probe.md`](artifacts/memory-probe.md)。

## 后续
- 单击/双击交互接入时需先处理与 `DragMove()` 的冲突（见 `MainWindow.xaml.cs` 注释）
