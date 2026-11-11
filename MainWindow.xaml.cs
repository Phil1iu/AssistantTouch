using System.Windows;
using System.Windows.Input;

namespace AssistiveTouch;

/// <summary>
/// 桌面悬浮球主窗口：无边框、置顶、半透明、可鼠标拖拽。
/// 当前为骨架，点击/双击等业务逻辑后续接入。
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 按住左键进入系统原生拖拽循环，可把悬浮球拖到屏幕任意位置。
    /// </summary>
    /// <remarks>
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
        }
        catch (InvalidOperationException)
        {
            // 鼠标键已提前释放、或窗口状态不满足拖拽前提时 DragMove 会抛异常；
            // 忽略即可，不能因此让悬浮球崩溃。
        }
    }

    /// <summary>
    /// 右键单击悬浮球：弹确认框，只有选「是」才退出程序。
    /// </summary>
    /// <remarks>
    /// 选右键而不是双击：DragMove() 会独占本次左键按压的后续事件（见上），
    /// 双击要先把手动拖拽改成位移阈值判定才能与单击区分；右键与左键拖拽天然互不干扰。
    /// 弹窗必须带 owner（this）：窗口是 Topmost + 无边框，无 owner 的弹窗可能被压在球后面。
    /// 「否」或直接关闭弹窗都不做任何事，保持原状。
    /// 退出走 WPF 正常关闭；不得用 Environment.Exit / Process.Kill 强杀。
    /// </remarks>
    private void OnMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        // 本次右键按压到此处理完毕，不再向冒泡链上游传递。
        e.Handled = true;

        MessageBoxResult result = MessageBox.Show(
            this,
            "退出 AssistiveTouch？",
            "AssistiveTouch",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            Application.Current.Shutdown();
        }
    }
}
