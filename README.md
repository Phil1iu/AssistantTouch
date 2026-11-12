# AssistantTouch

Windows 桌面**磨砂玻璃悬浮球**：单击展开边缘自适应的径向快捷菜单（可自定义应用快捷启动）。

## 技术栈
- C# / WPF / .NET 8（`global.json` 已锁定 SDK 8.0.204）

## 当前功能
- 无边框透明窗口（`WindowStyle=None` + `AllowsTransparency=True`）
- 常驻最顶层（`Topmost=True`）、不在任务栏显示（`ShowInTaskbar=False`）
- 72×72 **磨砂玻璃悬浮球**（纯 WPF 模拟，不用 DWM/SetWindowCompositionAttribute）
  - 底：半透明白 `#B8FFFFFF`（约 72%），能隐约透出背后桌面内容；悬停 `#D9FFFFFF`
  - 雾面：程序生成的细灰度噪点（`WriteableBitmap` → 平铺 `TileBrush`），不透明度常态 8%、悬停 10%，圆内裁剪
  - 高光：左上向柔和径向渐变白（`#40FFFFFF` → 透明）
  - 描边：内圈 1px 半透明白 `#80FFFFFF`（悬停 `#A6FFFFFF`）作玻璃边缘反光；外圈 1px `#59000000`（双层玻璃边，保证纯白/浅色桌面上的轮廓）
  - 无投影、无 2px 深色粗描边；悬停缩放 1.04；噪点画刷为静态资源，无每帧计算
  - 可访问性口径：白底外圈目标「最暗 ≥1.8:1、均值 ≥1.5:1」、黑底球体 ≥10:1；静态推算外圈 `#59000000` 在纯白上合成灰 166、对比 ≈2.43:1（旧 `#33000000` ≈1.61:1），实际像素以 tester VM 实测为准
- **左键交互**（手动拖拽，4px 阈值；不再使用 `DragMove()`）
  - 拖动超过 4 设备像素 → 移动球；拖动中持续钳制在最近显示器工作区内，松手不回弹
  - **未拖动（<4px）松开 → 打开径向快捷菜单**：以大圆为中心均匀分布快捷小圆（默认 6 个：显示桌面 / 截图 / 任务管理器 / 设置 / 文件资源管理器 / 锁定电脑）+「+」圆；点非圆区域或 Esc 关闭
  - 「+」选择 exe/lnk 添加应用（最多 8 个，超出提示）；右键小圆可「删除 / 更换程序 / 恢复默认」
  - 文件对话框打开前会先隐藏径向菜单，选择完成后重新显示；球靠近屏幕边缘/角落时不再隐藏槽位，而是按可用扇区把全部快捷项收拢成弧形（必要时内外双弧、半径自适应放大），保证所有小圆 +「+」都在工作区内、互不重叠、不压大球
  - 快捷方式配置：`%LOCALAPPDATA%\AssistantTouch\shortcuts.json`；缺失/损坏/字段非法时回落到 6 个默认项，不崩溃
  - 双击没有额外动作（不重置位置）
- **触摸**：与鼠标共用同一套拖拽状态机（4px 阈值、连续钳制、无回弹）
  - 单指拖动 → 移动球；未拖动的轻触不做任何动作
  - 多指时只由第一个触点驱动，额外触点忽略
  - 当前环境**无触摸屏，未实机验证**
- **右键菜单**（右键单击悬浮球，菜单显示在鼠标位置、压在球之上）
  - 浅色主题：白底、1px 灰边、6px 圆角、悬停 `#F2F2F2`、分隔线 `#E5E5E5`、文字 `#1A1A1A`
  - **重置位置**：回到主屏工作区中央，并立即写入位置文件
  - **关于**：显示名称、构建版本（优先取带 `+<commit>` 戳的 InformationalVersion）与仓库地址
  - **退出**：弹确认框「退出 AssistantTouch？」，**默认按钮为「否」**（直接回车不退出），选「是」才退出
  - 退出走 WPF 正常关闭 `Application.Current.Shutdown()`，不需要再 `taskkill`
- **托盘图标**（任务栏通知区）：白色圆球图标；双击切换悬浮球显示/隐藏
  - 托盘菜单：显示/隐藏悬浮球、重置位置、关于、退出
- **开机自启**：右键菜单「开机自启」可勾选/取消；写入 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`，值名 `AssistantTouch`，值为带引号的 exe 完整路径；只影响当前用户，不碰 HKLM
- **单实例**：同一 Windows 用户（**跨登录会话**）只允许一个进程；互斥量 `Global\AssistantTouch.SingleInstance.<SID>`，权限不足时回退 `Local\AssistantTouch.SingleInstance.<SID>`（回退时仅保证**同会话**单实例）；第二次启动直接退出，不创建窗口、不读写位置文件。不同用户可各运行一个实例。
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

## 快捷方式文件
- 路径：`%LOCALAPPDATA%\AssistantTouch\shortcuts.json`
- 内容：`{"shortcuts":[{"kind":"system","action":"showDesktop","name":"显示桌面"},{"kind":"app","path":"C:\\...\\x.exe","name":"X"}]}`
- 默认 6 个系统快捷方式；最多 8 个；文件缺失/损坏/字段非法时回落到默认，不崩溃
- 修改（添加/删除/更换/恢复默认）立即写盘，临时文件 + 覆盖移动
- **加载时机**：每次打开径向菜单都会重新读取该文件（不是只在启动时读一次），外部修改后无需重启
- **诊断日志**：每次加载覆盖写 `%LOCALAPPDATA%\AssistantTouch\shortcuts-load.log`，记录时间、路径、是否存在、长度、解析结果、条数、首个失败原因（含 JSON 异常消息）与内容预览
- 路径按 JSON 规则写双反斜杠（`"C:\\Windows\\System32\\notepad.exe"`）；非法 JSON（例如只写单反斜杠）会导致整份配置被拒并回落到 6 个默认项，失败原因见上面的日志

## 已知问题 / 待决策
- **内存验收口径冲突**：本机空 WPF 模板空闲即 227.9MB（工作集），本项目 207~231MB，
  冲刺账本「空闲内存 < 50MB」在 C#/WPF + 本机环境下不可达。
  取证与建议见 [`artifacts/memory-probe.md`](artifacts/memory-probe.md)。
- **PerMonitorV2（高 DPI）**：`app.manifest` 声明 PerMonitorV2，旧系统回落 `true/PM`；保存的位置按窗口所在显示器的 DPI 折算；钳制时按**目标显示器 DPI**（`GetDpiForMonitor`）折算工作区；跨显示器/DPI 变化通过 `DpiChanged` 重新钳制，拖拽中会重置指针与窗口基准避免跳变。混合 DPI 下的实际表现**未实机验证**（当前环境为 100%）。
- **单实例范围**：`Global\AssistantTouch.SingleInstance.<SID>` 按 Windows 用户隔离（同一用户跨会话互斥、不同用户各一个）；创建失败回退 `Local\...<SID>`，**此时只保证同一登录会话内的单实例**（跨会话不再互斥）；都失败则按「已有实例」退出。未做不同用户之间的互斥。

## 后续
- 左键拖拽 / 单击打开快捷操作面板；右键或托盘图标打开菜单。若日后调整单击语义，需要重新设计快捷面板与拖拽阈值的配合。
