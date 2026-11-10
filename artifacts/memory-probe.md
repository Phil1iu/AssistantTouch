# 内存实测与「< 50MB」验收线的冲突取证

- 记录人：winAssistiveTouch-writer
- 时间：2026-11-10 22:18 CST
- 触发原因：冲刺账本「验审标准 #1」与派单 D3（#4）要求**空闲内存 < 50MB**，而 writer 冒烟自测时发现 WPF 应用常驻内存远超该线，故先行取证，供 organizer / 负责人决策。

## 结论（先看这个）

1. 本机（Windows 11 26200，.NET 8 + WPF）**一个未经任何修改的 `dotnet new wpf` 空模板**，空闲即占用：
   - 工作集 **227.9 MB**，私有提交 **158.3 MB**。
2. 本项目的悬浮球（72×72、无边框置顶）实测：
   - 带投影：工作集 230.9 MB / 私有 162.9 MB（首测），207.4 MB / 151.5 MB（复测）
   - 去掉 `DropShadowEffect`：工作集 209.2 MB / 私有 150.8 MB
3. 即：**悬浮球自身开销 ≈ 0，甚至略低于空模板**；200MB+ 是这台机器上 WPF/.NET 8 的**底噪**。
4. **因此「空闲内存 < 50MB」在 C#/WPF 技术栈 + 本机环境下不可达**，任何实现都无法通过。这是技术栈选型（#1 拍板 C# WPF/.NET 8）与验收线（#1 验审标准）之间的**口径冲突**，不是实现缺陷。
5. 需要负责人二选一（见文末「待决策」）。

## 测量方法

- 进程：`bin/Release/net8.0-windows/AssistiveTouch.exe`，启动后**静置 8–10 秒**（无鼠标操作）再读数。
- 读数：`Get-Process <name> | WorkingSet64 / PrivateMemorySize64`；同一进程两次读数（间隔 10s）**完全一致**（230.9/162.9 两次相同），确认已稳定、非 GC 抖动。
- 进程存活且响应：`Responding=True`。

## 对照实验（证明是栈底噪而非本项目代码）

```powershell
# 1) 纯净基线：空 WPF 模板，未改一行代码
mkdir E:\tmp\wpf-baseline
cd E:\tmp\wpf-baseline
'{"sdk":{"version":"8.0.204","rollForward":"latestFeature"}}' | Out-File global.json -Encoding ascii
dotnet new wpf -n Baseline -o .
dotnet build -c Release
.\bin\Release\net8.0-windows\Baseline.exe     # 静置 9s 后读数 → WS 227.9MB / Private 158.3MB
```

```bash
# 2) 本项目
cd /c/repos/AssistiveTouch && dotnet build -c Release
./bin/Release/net8.0-windows/AssistiveTouch.exe   # 静置 8s 后读数
```

两组只差模板内容，运行环境、SDK、帧率、桌面会话完全一致。

## 数据表

| 对象 | 改动 | 工作集 WS | 私有提交 Private | 备注 |
| :--- | :--- | ---: | ---: | :--- |
| Baseline（空 WPF 模板） | 无 | 227.9 MB | 158.3 MB | 本机 WPF 地板 |
| AssistiveTouch | 带 DropShadowEffect | 230.9 MB | 162.9 MB | 首测 |
| AssistiveTouch | 带 DropShadowEffect | 207.4 MB | 151.5 MB | 复测（工作集随系统压力浮动） |
| AssistiveTouch | 去掉 DropShadowEffect | 209.2 MB | 150.8 MB | 投影开销落在噪声内 |

> 说明：工作集（Working Set）会随系统内存压力浮动，私有提交（Private Bytes）更稳定，二者此处都远高于 50MB。
> 若验收口径是任务管理器「内存」列（= 私有工作集），本项目预计在同一量级，仍不可能 < 50MB。

## 待决策（organizer / 负责人）

1. **改验收口径**（推荐）：把「绝对内存 < 50MB」改为**相对基线**，例如「悬浮球进程私有内存 ≤ 空 WPF 模板基线 + 20MB」。本实现当前即满足（151~163MB vs 基线 158MB）。
2. **换技术栈**：若 50MB 是硬指标，C#/WPF 不适用，需换 Win32/WinUI/Direct2D 类实现（但这会推翻 #1 的选型拍板，成本另计）。
3. **放宽到现实值**：例如「< 250MB」，但该值对产品无约束意义。

writer 不做选型决定，只提供取证；D1 范围内按派单把骨架与构建交付完毕。

## 附：本次运行的其他观测（窗口行为）

- 顶层窗口类 `HwndWrapper[AssistiveTouch;;…]`，标题 `AssistiveTouch`
- `visible=True`、`topmost=True`（扩展样式 `0x80008`，含 `WS_EX_TOPMOST` 与 `WS_EX_TOOLWINDOW`，后者由 `ShowInTaskbar=False` 带来）
- 尺寸 72×72，`CenterScreen` 生效（矩形 (924,480)-(996,552)）
- 进程响应正常，无异常退出
