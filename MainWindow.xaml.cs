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
}
