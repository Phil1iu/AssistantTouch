using System.ComponentModel;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace AssistantTouch;

/// <summary>
/// 桌面悬浮球主窗口：无边框、置顶、半透明、可鼠标拖拽，并记忆上次位置。
/// 左键单击打开菜单、双击回到主屏中央；拖拽由本类手动实现（4px 阈值）。
/// </summary>
public partial class MainWindow : Window
{
    /// <summary>超过该设备像素位移才判定为拖拽，否则视为单击/轻触。</summary>
    private const double DragThresholdPixels = 4;

    /// <summary>系统双击时间（毫秒）；读取失败时的回退值。</summary>
    private const int FallbackDoubleClickMilliseconds = 500;

    [DllImport("user32.dll")]
    private static extern uint GetDoubleClickTime();

    private enum PointerSource
    {
        None,
        Mouse,
        Touch
    }

    private readonly DispatcherTimer _singleClickTimer;

    private Point _pointerDownScreen;
    private Point _windowAtPointerDown;
    private bool _isPointerDown;
    private bool _isDragging;
    private PointerSource _activeSource;
    private int _activeTouchId = -1;
    private bool _touchDoubleTapPending;
    private bool _pendingTapWasTouch;

    public MainWindow()
    {
        InitializeComponent();

        _singleClickTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(GetSingleClickDelayMilliseconds())
        };
        _singleClickTimer.Tick += OnSingleClickTimerTick;
        Loaded += OnWindowLoaded;

        RestoreSavedPosition();
    }

    /// <summary>
    /// 单击延迟取系统双击时间（<c>GetDoubleClickTime</c>），钳制到 200–1000ms：
    /// 保证系统判定为双击的第二击总能先进入 <c>ClickCount == 2</c> 分支，不会先弹出菜单。
    /// </summary>
    private static int GetSingleClickDelayMilliseconds()
    {
        int systemDelay = (int)GetDoubleClickTime();
        if (systemDelay <= 0)
        {
            return FallbackDoubleClickMilliseconds;
        }

        return Math.Clamp(systemDelay, 200, 1000);
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

        // 启动钳制后若值变化，立即回写：避免强杀后仍留着越界的旧值。
        if (clamped.X != savedLeft || clamped.Y != savedTop)
        {
            WindowPositionStore.Save(clamped.X, clamped.Y);
        }
    }

    /// <summary>
    /// 钳制到最近显示器工作区；只有位置真的变化时才回写。
    /// </summary>
    private void ClampBallToNearestMonitorAndSaveIfMoved()
    {
        double oldLeft = Left;
        double oldTop = Top;
        ClampBallToNearestMonitor();

        if (Left != oldLeft || Top != oldTop)
        {
            WindowPositionStore.Save(Left, Top);
        }
    }

    /// <summary>
    /// 拖拽过程中/结束后把窗口完整钳进最近显示器工作区，避免把球拖到屏幕外后找不回来。
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

        return WindowClamp.Clamp(desiredLeft, desiredTop, Width, Height, workArea);
    }

    /// <summary>正常关闭窗口时保存当前位置（位置文件写失败不影响退出）。</summary>
    protected override void OnClosing(CancelEventArgs e)
    {
        CancelPendingClick();
        WindowPositionStore.Save(Left, Top);
        base.OnClosing(e);
    }

    /// <summary>
    /// 首次显示后，用最终（可能是目标显示器）的 DPI 再钳制一次并保存。
    /// </summary>
    /// <remarks>
    /// 构造期恢复位置时窗口还没有 PresentationSource，PerMonitorV2 下的 DPI 可能还是系统值；
    /// 显示后再钳制一次，避免在混合 DPI 布局里把球放到目标显示器之外。
    /// </remarks>
    private void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        ClampBallToNearestMonitorAndSaveIfMoved();
    }

    /// <summary>
    /// 跨显示器导致 DPI 变化：按新 DPI 重新计算所在显示器工作区并钳制，保证 72 DIU 的球完整可见。
    /// </summary>
    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        ClampBallToNearestMonitorAndSaveIfMoved();
    }

    /// <summary>
    /// 鼠标/触摸共用的按下处理：记录起点与窗口位置，标记当前输入源。
    /// </summary>
    private void BeginPointerInteraction(PointerSource source, Point screenPoint)
    {
        _activeSource = source;
        _isPointerDown = true;
        _isDragging = false;
        _pointerDownScreen = screenPoint;
        _windowAtPointerDown = new Point(Left, Top);
    }

    /// <summary>
    /// 鼠标/触摸共用的移动处理：位移超过 4 设备像素才进入拖拽，拖动中按 DPI 折算并连续钳制。
    /// </summary>
    /// <remarks>
    /// 位置始终由「按下时的窗口位置 + 累计位移」计算（绝对量），
    /// 因此拖到边缘被钳住后，反向移动会立刻跟手，不会出现「粘滞/回弹」。
    /// </remarks>
    private void UpdatePointerInteraction(Point currentScreen)
    {
        if (!_isPointerDown)
        {
            return;
        }

        Vector delta = currentScreen - _pointerDownScreen;

        if (!_isDragging &&
            (Math.Abs(delta.X) > DragThresholdPixels || Math.Abs(delta.Y) > DragThresholdPixels))
        {
            _isDragging = true;
        }

        if (_isDragging)
        {
            DpiScale dpi = VisualTreeHelper.GetDpi(this);
            Left = _windowAtPointerDown.X + delta.X / dpi.DpiScaleX;
            Top = _windowAtPointerDown.Y + delta.Y / dpi.DpiScaleY;
            ClampBallToNearestMonitor();
        }
    }

    /// <summary>结束一次鼠标/触摸交互，返回是否发生过拖拽。</summary>
    private bool EndPointerInteraction()
    {
        bool wasDragging = _isDragging;
        _isPointerDown = false;
        _isDragging = false;
        _activeSource = PointerSource.None;
        _activeTouchId = -1;
        return wasDragging;
    }

    /// <summary>拖拽结束后：钳制到最近显示器工作区并保存。</summary>
    private void FinishDrag()
    {
        ClampBallToNearestMonitor();
        WindowPositionStore.Save(Left, Top);
    }

    /// <summary>
    /// 左键按下：第二次点击（ClickCount == 2）直接执行双击动作；否则记录起点并准备手动拖拽。
    /// </summary>
    /// <remarks>
    /// 不再使用 <c>DragMove()</c>：它独占鼠标并吞掉后续点击，无法与单击/双击共存。
    /// 触摸进行中时忽略触摸提升出来的兼容鼠标事件（由 <see cref="_activeSource"/> 区分）。
    /// </remarks>
    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // 触摸提升的兼容鼠标事件一律忽略：触摸由 Touch 路径负责。
        if (e.StylusDevice?.TabletDevice.Type == TabletDeviceType.Touch)
        {
            e.Handled = true;
            return;
        }

        // 输入源互斥必须优先于双击判断：触摸进行中不接受鼠标双击。
        if (_activeSource != PointerSource.None)
        {
            e.Handled = true;
            return;
        }

        if (e.ClickCount == 2)
        {
            CancelPendingClick();
            ResetToPrimaryCenter();
            e.Handled = true;
            return;
        }

        _singleClickTimer.Stop();
        BeginPointerInteraction(PointerSource.Mouse, PointToScreen(e.GetPosition(this)));

        // 必须捕获到 Grid 而不是 Window：WPF 捕获后鼠标事件只路由到捕获元素，
        // 处理器在 Grid 上，若捕获 Window，后续 MouseMove/MouseLeftButtonUp 不会到达 Grid。
        if (!RootGrid.CaptureMouse())
        {
            // 捕获失败：回退状态，不进入拖拽/单击，避免状态卡住。
            EndPointerInteraction();
            e.Handled = true;
            return;
        }

        e.Handled = true;
    }

    /// <summary>左键拖动；若捕获仍在但左键已松开（Up 丢失），按拖拽结束处理。</summary>
    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (_activeSource != PointerSource.Mouse)
        {
            return;
        }

        if (e.LeftButton != MouseButtonState.Pressed)
        {
            bool wasDragging = EndPointerInteraction();

            if (RootGrid.IsMouseCaptured)
            {
                RootGrid.ReleaseMouseCapture();
            }

            if (wasDragging)
            {
                FinishDrag();
            }

            e.Handled = true;
            return;
        }

        UpdatePointerInteraction(PointToScreen(e.GetPosition(this)));
        e.Handled = true;
    }

    /// <summary>
    /// 左键抬起：拖拽过则钳制并保存；没拖拽则延时到系统双击时间后视为单击（打开菜单）。
    /// </summary>
    private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_activeSource != PointerSource.Mouse)
        {
            return;
        }

        bool wasDragging = EndPointerInteraction();

        if (RootGrid.IsMouseCaptured)
        {
            RootGrid.ReleaseMouseCapture();
        }

        if (wasDragging)
        {
            FinishDrag();
        }
        else
        {
            _pendingTapWasTouch = false;
            _singleClickTimer.Start();
        }

        e.Handled = true;
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        if (_activeSource == PointerSource.Mouse)
        {
            EndPointerInteraction();
            CancelPendingClick();
        }

        base.OnLostMouseCapture(e);
    }

    /// <summary>触摸捕获丢失（被系统/其他元素夺走）：清理状态，避免后续输入被忽略。</summary>
    private void OnLostTouchCapture(object sender, TouchEventArgs e)
    {
        if (_activeSource == PointerSource.Touch && e.TouchDevice.Id == _activeTouchId)
        {
            EndPointerInteraction();
            CancelPendingClick();
        }
    }

    /// <summary>右键按下：停止尚未触发的单击计时器，避免与右键菜单重复打开。</summary>
    private void OnMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        CancelPendingClick();
    }

    /// <summary>取消尚未触发的单击/轻触（右键、关闭、托盘隐藏、捕获丢失时调用）。</summary>
    internal void CancelPendingClick()
    {
        _singleClickTimer.Stop();
        _pendingTapWasTouch = false;
        _touchDoubleTapPending = false;
    }

    /// <summary>
    /// 触摸按下：只接受第一个触点，其余触点忽略；与鼠标共用同一套起点/阈值/钳制状态。
    /// </summary>
    private void OnTouchDown(object sender, TouchEventArgs e)
    {
        if (_activeSource != PointerSource.None)
        {
            // 已有鼠标或触摸在驱动交互，忽略额外触点。
            e.Handled = true;
            return;
        }

        // 若上一次「触摸」轻触留下的菜单计时器还未到点，这一次按下就是触摸双击。
        if (_singleClickTimer.IsEnabled && _pendingTapWasTouch)
        {
            _singleClickTimer.Stop();
            _touchDoubleTapPending = true;
        }
        else
        {
            _singleClickTimer.Stop();
            _touchDoubleTapPending = false;
        }

        _activeTouchId = e.TouchDevice.Id;
        BeginPointerInteraction(PointerSource.Touch, PointToScreen(e.GetTouchPoint(this).Position));

        if (!RootGrid.CaptureTouch(e.TouchDevice))
        {
            // 捕获失败：回退状态，不进入拖拽/轻触。
            _touchDoubleTapPending = false;
            EndPointerInteraction();
            e.Handled = true;
            return;
        }

        e.Handled = true;
    }

    /// <summary>触摸移动：只处理当前主触点。</summary>
    private void OnTouchMove(object sender, TouchEventArgs e)
    {
        if (_activeSource != PointerSource.Touch || e.TouchDevice.Id != _activeTouchId)
        {
            e.Handled = true;
            return;
        }

        UpdatePointerInteraction(PointToScreen(e.GetTouchPoint(this).Position));
        e.Handled = true;
    }

    /// <summary>
    /// 触摸抬起：拖拽过则钳制保存；轻触在系统双击时间后打开菜单；双击则回到主屏中央。
    /// </summary>
    private void OnTouchUp(object sender, TouchEventArgs e)
    {
        if (_activeSource != PointerSource.Touch || e.TouchDevice.Id != _activeTouchId)
        {
            e.Handled = true;
            return;
        }

        bool wasDragging = EndPointerInteraction();
        RootGrid.ReleaseTouchCapture(e.TouchDevice);

        if (wasDragging)
        {
            FinishDrag();
        }
        else if (_touchDoubleTapPending)
        {
            // 触摸双击：回到主屏工作区中央并保存。
            _touchDoubleTapPending = false;
            ResetToPrimaryCenter();
        }
        else
        {
            // 轻触：与单击一致，延时到系统双击时间后打开菜单（留出双击判定窗口）。
            _pendingTapWasTouch = true;
            _singleClickTimer.Start();
        }

        e.Handled = true;
    }

    /// <summary>在球下方打开右键菜单（鼠标单击延时到点、触摸轻触共用）。</summary>
    private void OpenBallMenu()
    {
        if (RootGrid.ContextMenu is ContextMenu menu)
        {
            menu.PlacementTarget = this;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            menu.IsOpen = true;
        }
    }

    /// <summary>单击延时到点：在球下方打开同一个右键菜单。</summary>
    private void OnSingleClickTimerTick(object? sender, EventArgs e)
    {
        _singleClickTimer.Stop();
        _pendingTapWasTouch = false;
        OpenBallMenu();
    }

    /// <summary>
    /// 把悬浮球移回主屏工作区中央并立即落盘；菜单「重置位置」与双击共用。
    /// </summary>
    internal void ResetToPrimaryCenter()
    {
        Rect workArea = SystemParameters.WorkArea;
        Left = workArea.Left + (workArea.Width - Width) / 2;
        Top = workArea.Top + (workArea.Height - Height) / 2;
        ClampBallToNearestMonitor();
        WindowPositionStore.Save(Left, Top);
    }

    /// <summary>右键菜单「重置位置」。</summary>
    private void OnResetPositionClick(object sender, RoutedEventArgs e)
    {
        ResetToPrimaryCenter();
    }

    /// <summary>菜单打开时按 HKCU 注册表的实际状态刷新「开机自启」勾选。</summary>
    private void OnBallMenuOpened(object? sender, RoutedEventArgs e)
    {
        CancelPendingClick();

        if (FindMenuItem("开机自启") is MenuItem item)
        {
            item.IsChecked = AutostartService.IsEnabled();
        }
    }

    /// <summary>切换开机自启；以注册表读回的实际状态为准（写失败时勾选会回弹）。</summary>
    private void OnAutostartClick(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem item)
        {
            AutostartService.SetEnabled(item.IsChecked);
            item.IsChecked = AutostartService.IsEnabled();
        }
    }

    private MenuItem? FindMenuItem(string header)
    {
        if (RootGrid.ContextMenu is not ContextMenu menu)
        {
            return null;
        }

        foreach (object entry in menu.Items)
        {
            if (entry is MenuItem item && string.Equals(item.Header as string, header, StringComparison.Ordinal))
            {
                return item;
            }
        }

        return null;
    }

    /// <summary>
    /// 显示「关于」对话框；右键菜单与托盘菜单共用。
    /// </summary>
    internal void ShowAboutDialog()
    {
        string version = GetDisplayVersion();
        MessageBox.Show(
            this,
            $"AssistantTouch\n版本 {version}\nhttps://github.com/Phil1iu/AssistantTouch",
            "关于",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    /// <summary>右键菜单「关于」。</summary>
    private void OnAboutClick(object sender, RoutedEventArgs e)
    {
        ShowAboutDialog();
    }

    /// <summary>
    /// 请求退出：弹确认框（默认「否」），只有选「是」才正常关闭。右键菜单与托盘菜单共用。
    /// </summary>
    /// <remarks>
    /// 选「否」不做任何事，保持原状；Yes/No 对话框没有取消按钮，Esc 与标题栏关闭均不可用（rev-02 实测）。
    /// 退出走 WPF 正常关闭；不得用 Environment.Exit / Process.Kill 强杀。
    /// </remarks>
    internal void RequestExit()
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

    /// <summary>右键菜单「退出」。</summary>
    private void OnExitClick(object sender, RoutedEventArgs e)
    {
        RequestExit();
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
}
