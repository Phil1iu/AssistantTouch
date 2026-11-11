using System.ComponentModel;
using System.Reflection;
using System.Windows;
using System.Windows.Input;

namespace AssistiveTouch;

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
    /// 保存过已断开显示器的坐标时，按虚拟桌面把窗口完整钳回来。
    /// </remarks>
    private void RestoreSavedPosition()
    {
        if (!WindowPositionStore.TryLoad(out double savedLeft, out double savedTop))
        {
            return;
        }

        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = ClampToVirtualScreen(savedLeft, SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenWidth);
        Top = ClampToVirtualScreen(savedTop, SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenHeight);
    }

    /// <summary>
    /// 把坐标钳进虚拟桌面，保证窗口（含 72px 全宽）完整落在所有显示器组成的区域内。
    /// </summary>
    /// <remarks>
    /// 虚拟桌面比窗口还小时上界会小于下界，此时结果落在 <paramref name="origin"/>，仍然完整可见。
    /// </remarks>
    private double ClampToVirtualScreen(double value, double origin, double size)
    {
        double max = origin + size - Width;
        return Math.Max(origin, Math.Min(max, value));
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
    /// 拖拽结束（<c>DragMove()</c> 返回）立刻保存新位置：这样即使进程随后被强杀，
    /// 下次启动也能回到拖到的地方，而不是只在正常退出时才落盘。
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
    /// 右键菜单「关于」：显示名称、程序集版本与仓库地址。
    /// </summary>
    /// <remarks>
    /// 弹窗必须带 owner（this）：窗口是 Topmost + 无边框，无 owner 的弹窗可能被压在球后面。
    /// </remarks>
    private void OnAboutClick(object sender, RoutedEventArgs e)
    {
        Version? version = Assembly.GetExecutingAssembly().GetName().Version;
        MessageBox.Show(
            this,
            $"AssistiveTouch\n版本 {version?.ToString() ?? "未知"}\nhttps://github.com/Phil1iu/AssistiveTouch",
            "关于",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
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
            "退出 AssistiveTouch？",
            "AssistiveTouch",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question,
            MessageBoxResult.No);

        if (result == MessageBoxResult.Yes)
        {
            Application.Current.Shutdown();
        }
    }
}
