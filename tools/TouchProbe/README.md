# TouchProbe · 触摸注入探针

独立控制台工具，用于验证 Windows **Touch Injection API**（`InitializeTouchInjection` /
`InjectTouchInput`）能否在当前会话与权限下成功注入触摸，供 tester 在 VM 内驱动
AssistantTouch 的 WM_TOUCH 路径。

- **不启动、不附加、不引用 AssistantTouch**；不修改主程序运行路径。
- **默认不注入**：无参数只做结构体布局自检并打印用法。
- 本工具不会绕过系统策略；注入失败时只报告 `GetLastError`。

## 构建 / 发布

```powershell
# 开发构建
dotnet build tools\TouchProbe\TouchProbe.csproj -c Release

# self-contained 发布（给 tester 的 VM 用，无需 .NET 运行时）
dotnet publish tools\TouchProbe\TouchProbe.csproj -c Release -r win-x64 --self-contained true -o dist\tools\TouchProbe
```

发布产物入口：`dist\tools\TouchProbe\TouchProbe.exe`。

## 用法

```text
TouchProbe                  打印尺寸/偏移与用法（不注入）
TouchProbe sizes            同上（不注入）
TouchProbe probe            初始化 + 向 (100,100) 注入一次 down/up
TouchProbe tap x y          一次点击（down → 60ms → up）
TouchProbe drag x1 y1 x2 y2 [steps]
                            按下、逐步移动（默认 10 步）、抬起
```

- 坐标是**物理屏幕像素**（虚拟桌面坐标系），多显示器可用负坐标。
- 触摸点 `pointerId=1`、`pointerType=PT_TOUCH(2)`、`frameId=0`；
  接触面积 `rcContact` 为以点为中心 4×4（`TOUCH_MASK_CONTACTAREA`）。
- 标志位：首点 `DOWN|INRANGE|INCONTACT`，移动 `UPDATE|INRANGE|INCONTACT`，
  抬起 `UP|INRANGE`。
- 初始化首选 `(10, POINTER_FEEDBACK_DEFAULT=1)`；若失败会依次尝试 `(1,0)`、`(10,0)`，
  每次失败都打印错误码；全部失败则不注入。

## 布局自检（错误 87 排查）

启动时打印 `Marshal.SizeOf` 与关键字段偏移，并和 Windows SDK 期望值逐项对照；
**不一致时拒绝注入**（退出码 5）。

| 结构体 | x64 | x86 |
| :--- | ---: | ---: |
| `POINTER_INFO` | **96** | 88 |
| `POINTER_TOUCH_INFO` | **144** | 136 |

x64 关键偏移（`Marshal.OffsetOf`）：

| 字段 | 偏移 | 字段 | 偏移 |
| :--- | ---: | :--- | ---: |
| `pointerType` | 0 | `sourceDevice` | 16 |
| `pointerId` | 4 | `hwndTarget` | 24 |
| `frameId` | 8 | `ptPixelLocation` | 32 |
| `pointerFlags` | 12 | `ptHimetricLocation` | 40 |
| `ptPixelLocationRaw` | 48 | `ptHimetricLocationRaw` | 56 |
| `dwTime` | 64 | `historyCount` | 68 |
| `InputData` | 72 | `dwKeyStates` | 76 |
| `PerformanceCount` | 80 | `ButtonChangeType` | 88 |
| `touchFlags` | 96 | `touchMask` | 100 |
| `rcContact` | 104 | `rcContactRaw` | 120 |
| `orientation` | 136 | `pressure` | 140 |

常见导致 `InjectTouchInput` 返回 `FALSE` + `ERROR_INVALID_PARAMETER (87)` 的布局错误：

1. `POINTER_INFO` 漏了 `ptHimetricLocation` / `ptPixelLocationRaw` / `ptHimetricLocationRaw`
   （只写一个 `POINT` 会让后续字段全部错位）；
2. `PerformanceCount` 用 `uint`/`int` 而不是 `ulong`；
3. `sourceDevice` / `hwndTarget` 用 `int` 而不是 `IntPtr`（x64 下错位）；
4. `POINTER_TOUCH_INFO` 漏了 `rcContactRaw`，或把 `touchFlags`/`touchMask` 写成 `bool`；
5. 以 32 位进程运行却套用 x64 布局。
6. 首点未使用 `DOWN|INRANGE|INCONTACT`，或后续未使用 `UPDATE|INRANGE|INCONTACT`。

## 退出码

| 码 | 含义 |
| ---: | :--- |
| 0 | 成功 / 仅自检 |
| 2 | 参数错误 |
| 3 | `InitializeTouchInjection` 全部失败 |
| 4 | `InjectTouchInput` 失败（含清理 UP 失败） |
| 5 | 结构体布局与预期不符（拒绝注入） |

## 已知限制

- 必须在**交互式桌面会话**内运行（VM 的 session 1）；session 0 服务会话无法注入。
- 需要系统允许触摸注入；被组策略/安全软件拦截时会返回错误码，本工具不绕过。
- 不包含断言/截图能力；请由 tester 的脚本观察球位置、径向菜单与 `window.json` 变化。
- 坐标为物理像素；应用为 PerMonitorV2，系统负责把注入点映射到窗口坐标。
