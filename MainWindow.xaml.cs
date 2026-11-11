using System.ComponentModel;
using System.Reflection;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace AssistantTouch;

/// <summary>
/// 桌面悬浮球主窗口：无边框、置顶、半透明、可鼠标拖拽，并记忆上次位置。
/// 当前为骨架，点击/双击等业务逻辑后续接入。
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        RestoreSavedPosition();
    }

    /// <summary>
    /// 恢复上次保存的位置。没有可用位置时保持 XAML 的 <c>CenterScreen</c>。
    /// </summary>
    /// <remarks>
    /// 必须在 <c>InitializeComponent()</c> 之后、窗口显示之前执行：
    /// 先切到 <c>Manual</c> 再设 Left/Top，窗口才不会先居中再跳一次。
    /// 保存位置会按「窗口中心点所在（或最近）显示器」的工作区钳制，
    /// 因此双屏错位死区里只剩 21px 可见的旧坐标会被移回最近显示器。
    /// </remarks>
    private void RestoreSavedPosition()
    {
        if (!WindowPositionStore.TryLoad(out double savedLeft, out double savedTop))
        {
            return;
        }

        WindowStartupLocation = WindowStartupLocation.Manual;
        Point clamped = ClampToNearestMonitor(savedLeft, savedTop);
        Left = clamped.X;
        Top = clamped.Y;
    }

    /// <summary>
    /// 拖拽结束后把窗口完整钳进最近显示器工作区，避免把球拖到屏幕外后找不回来。
    /// </summary>
    private void ClampBallToNearestMonitor()
    {
        Point clamped = ClampToNearestMonitor(Left, Top);
        Left = clamped.X;
        Top = clamped.Y;
    }

    /// <summary>
    /// 把窗口左上角钳进「窗口中心点所在（或最近）显示器」的工作区，返回钳制后的左上角。
    /// </summary>
    /// <remarks>
    /// 用窗口中心点判定归属显示器：跨屏拖动时按球心计；中心落在所有显示器之外时，
    /// Win32 <c>MONITOR_DEFAULTTONEAREST</c> 取最近显示器。
    /// 若工作区比窗口还小（极端情况），上界小于下界，结果落在工作区左上角。
    /// </remarks>
    private Point ClampToNearestMonitor(double desiredLeft, double desiredTop)
    {
        DpiScale dpi = VisualTreeHelper.GetDpi(this);
        Rect workArea = MonitorLayout.GetWorkAreaAt(
            desiredLeft + Width / 2,
            desiredTop + Height / 2,
            dpi);

        double left = Math.Max(workArea.Left, Math.Min(workArea.Right - Width, desiredLeft));
        double top = Math.Max(workArea.Top, Math.Min(workArea.Bottom - Height, desiredTop));
        return new Point(left, top);
    }

    /// <summary>正常关闭窗口时保存当前位置（位置文件写失败不影响退出）。</summary>
    protected override void OnClosing(CancelEventArgs e)
    {
        WindowPositionStore.Save(Left, Top);
        base.OnClosing(e);
    }

    /// <summary>
    /// 按住左键进入系统原生拖拽循环，可把悬浮球拖到屏幕任意位置。
    /// </summary>
    /// <remarks>
    /// 拖拽结束（<c>DragMove()</c> 返回）先把球钳进最近显示器工作区（拖到屏幕边缘/外面松手也不会丢），
    /// 再保存新位置：这样即使进程随后被强杀，下次启动也能回到拖到的地方。
    /// 冲突预警：DragMove() 会独占鼠标并阻塞本次按压的后续事件，因此日后再加入
    /// “单击打开面板 / 双击”时不能与 DragMove 直接并存。届时应改为手动记录按下
    /// 坐标与位移阈值（例如 4px）：超过阈值才移动窗口，未超过则判定为单击；
    /// 否则单击会被吞掉，或被误判为拖拽。
    /// </remarks>
    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        try
        {
            DragMove();
            ClampBallToNearestMonitor();
            WindowPositionStore.Save(Left, Top);
        }
        catch (InvalidOperationException)
        {
            // 鼠标键已提前释放、或窗口状态不满足拖拽前提时 DragMove 会抛异常；
            // 忽略即可，不能因此让悬浮球崩溃。
        }
    }

    /// <summary>
    /// 右键菜单「重置位置」：把悬浮球移回主屏工作区中央，并立即落盘。
    /// </summary>
    /// <remarks>
    /// 用 <c>SystemParameters.WorkArea</c>（主屏工作区，已排除任务栏），
    /// 与「首次运行居中」的观感一致；重置后立刻写文件，重启仍落在中央。
    /// </remarks>
    private void OnResetPositionClick(object sender, RoutedEventArgs e)
    {
        Rect workArea = SystemParameters.WorkArea;
        Left = workArea.Left + (workArea.Width - Width) / 2;
        Top = workArea.Top + (workArea.Height - Height) / 2;
        WindowPositionStore.Save(Left, Top);
    }

    /// <summary>
    /// 右键菜单「关于」：显示名称、构建版本与仓库地址。
    /// </summary>
    /// <remarks>
    /// 弹窗必须带 owner（this）：窗口是 Topmost + 无边框，无 owner 的弹窗可能被压在球后面。
    /// 版本优先取带构建戳的 <see cref="AssemblyInformationalVersionAttribute"/>，
    /// 便于用户报障时定位具体 commit；取不到再回落程序集版本。
    /// </remarks>
    private void OnAboutClick(object sender, RoutedEventArgs e)
    {
        string version = GetDisplayVersion();
        MessageBox.Show(
            this,
            $"AssistantTouch\n版本 {version}\nhttps://github.com/Phil1iu/AssistantTouch",
            "关于",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    /// <summary>
    /// 取「关于」要显示的版本：优先 InformationalVersion（如 <c>1.0.0+8e4f46d…</c>），否则回落 AssemblyVersion。
    /// </summary>
    private static string GetDisplayVersion()
    {
        Assembly assembly = Assembly.GetExecutingAssembly();
        string? informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
        {
            return informational;
        }

        return assembly.GetName().Version?.ToString() ?? "未知";
    }

    /// <summary>
    /// 右键菜单「退出」：弹确认框，只有选「是」才退出程序。
    /// </summary>
    /// <remarks>
    /// 默认按钮为「否」（rev-02 的 P3）：误按回车不会退出，必须明确选「是」。
    /// 选「否」不做任何事，保持原状；Yes/No 对话框没有取消按钮，Esc 与标题栏关闭均不可用（rev-02 实测）。
    /// 退出走 WPF 正常关闭；不得用 Environment.Exit / Process.Kill 强杀。
    /// </remarks>
    private void OnExitClick(object sender, RoutedEventArgs e)
    {
        MessageBoxResult result = MessageBox.Show(
            this,
            "退出 AssistantTouch？",
            "AssistantTouch",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question,
            MessageBoxResult.No);

        if (result == MessageBoxResult.Yes)
        {
            Application.Current.Shutdown();
        }
    }
}
