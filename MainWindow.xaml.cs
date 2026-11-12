using System.ComponentModel;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace AssistantTouch;

/// <summary>
/// 桌面悬浮球主窗口：无边框、置顶、半透明、可鼠标/触摸拖拽，并记忆上次位置。
/// 左键只用于拖拽（未拖动的按下/松开不执行任何动作）；右键菜单与托盘菜单提供重置、自启、关于、退出。
/// </summary>
public partial class MainWindow : Window
{
    /// <summary>超过该设备像素位移才进入拖拽。</summary>
    private const double DragThresholdPixels = 4;

    private enum PointerSource
    {
        None,
        Mouse,
        Touch
    }

    private Point _pointerDownScreen;
    private Point _lastPointerScreen;
    private Point _windowAtPointerDown;
    private bool _isPointerDown;
    private bool _isDragging;
    private PointerSource _activeSource;
    private int _activeTouchId = -1;
    private RadialMenuWindow? _radialMenu;
    private ShortcutStore _shortcutStore;

    public MainWindow()
    {
        InitializeComponent();
        _shortcutStore = ShortcutStore.Load();
        Loaded += OnWindowLoaded;
        PreviewKeyDown += OnWindowPreviewKeyDown;
        RestoreSavedPosition();
    }

    /// <summary>径向菜单打开时，Esc 也可由仍持有焦点的球窗口关闭。</summary>
    private void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_radialMenu is not null && e.Key == Key.Escape)
        {
            _radialMenu.Close();
            _radialMenu = null;
            e.Handled = true;
        }
    }

    /// <summary>
    /// 恢复上次保存的位置。没有可用位置时保持 XAML 的 <c>CenterScreen</c>。
    /// </summary>
    /// <remarks>
    /// 必须在 <c>InitializeComponent()</c> 之后、窗口显示之前执行：
    /// 先切到 <c>Manual</c> 再设 Left/Top，窗口才不会先居中再跳一次。
    /// 保存位置会按「窗口中心点所在（或最近）显示器」的工作区钳制。
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

    /// <summary>钳制到最近显示器工作区；只有位置真的变化时才回写。</summary>
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
    /// 先把当前窗口 DIU 换算成物理像素，用物理中心点选目标显示器（<c>MonitorFromPoint</c> 用物理像素）；
    /// 目标工作区按目标显示器 DPI 折算，期望位置也换算到目标 DPI 后再钳制。
    /// 100% DPI 下与旧实现完全一致；混合 DPI 未实机验证。
    /// </remarks>
    private Point ClampToNearestMonitor(double desiredLeft, double desiredTop)
    {
        DpiScale currentDpi = VisualTreeHelper.GetDpi(this);

        double physicalLeft = desiredLeft * currentDpi.DpiScaleX;
        double physicalTop = desiredTop * currentDpi.DpiScaleY;
        double physicalCenterX = physicalLeft + Width * currentDpi.DpiScaleX / 2;
        double physicalCenterY = physicalTop + Height * currentDpi.DpiScaleY / 2;

        MonitorLayout.MonitorTarget target =
            MonitorLayout.GetNearestTarget(physicalCenterX, physicalCenterY, currentDpi);

        double targetLeft = physicalLeft / target.Dpi.DpiScaleX;
        double targetTop = physicalTop / target.Dpi.DpiScaleY;

        return WindowClamp.Clamp(targetLeft, targetTop, Width, Height, target.WorkArea);
    }

    /// <summary>关闭径向菜单（托盘隐藏、窗口关闭时调用）。</summary>
    internal void CloseRadialMenu()
    {
        if (_radialMenu is not null)
        {
            _radialMenu.Close();
            _radialMenu = null;
        }
    }

    /// <summary>正常关闭窗口时保存当前位置（位置文件写失败不影响退出）。</summary>
    protected override void OnClosing(CancelEventArgs e)
    {
        CloseRadialMenu();
        WindowPositionStore.Save(Left, Top);
        base.OnClosing(e);
    }

    /// <summary>首次显示后，用最终（可能是目标显示器）的 DPI 再钳制一次并保存。</summary>
    private void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        ClampBallToNearestMonitorAndSaveIfMoved();
    }

    /// <summary>
    /// 跨显示器导致 DPI 变化：按新 DPI 重新计算工作区并钳制；拖拽中重置指针/窗口基准，避免跳变。
    /// </summary>
    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);

        if (_isPointerDown)
        {
            ClampBallToNearestMonitor();
            _pointerDownScreen = _lastPointerScreen;
            _windowAtPointerDown = new Point(Left, Top);
        }
        else
        {
            ClampBallToNearestMonitorAndSaveIfMoved();
        }
    }

    /// <summary>鼠标/触摸共用的按下处理：记录起点与窗口位置，标记当前输入源。</summary>
    private void BeginPointerInteraction(PointerSource source, Point screenPoint)
    {
        _activeSource = source;
        _isPointerDown = true;
        _isDragging = false;
        _pointerDownScreen = screenPoint;
        _lastPointerScreen = screenPoint;
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

        _lastPointerScreen = currentScreen;
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
    /// 左键按下：准备拖拽。未拖动的松开不执行任何动作（不再打开菜单）。
    /// </summary>
    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // 触摸提升的兼容鼠标事件一律忽略：触摸由 Touch 路径负责。
        if (e.StylusDevice?.TabletDevice.Type == TabletDeviceType.Touch)
        {
            e.Handled = true;
            return;
        }

        if (_activeSource != PointerSource.None)
        {
            e.Handled = true;
            return;
        }

        BeginPointerInteraction(PointerSource.Mouse, PointToScreen(e.GetPosition(this)));

        // 必须捕获到 Grid 而不是 Window：WPF 捕获后鼠标事件只路由到捕获元素。
        if (!RootGrid.CaptureMouse())
        {
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

    /// <summary>左键抬起：拖拽过则钳制并保存；未拖动（<4px）则打开快捷操作面板。</summary>
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
            OpenRadialMenu();
        }

        e.Handled = true;
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        if (_activeSource == PointerSource.Mouse)
        {
            EndPointerInteraction();
        }

        base.OnLostMouseCapture(e);
    }

    /// <summary>触摸捕获丢失（被系统/其他元素夺走）：清理状态，避免后续输入被忽略。</summary>
    private void OnLostTouchCapture(object sender, TouchEventArgs e)
    {
        if (_activeSource == PointerSource.Touch && e.TouchDevice.Id == _activeTouchId)
        {
            EndPointerInteraction();
        }
    }

    /// <summary>触摸按下：只接受第一个触点；未拖动的抬起不做任何动作。</summary>
    private void OnTouchDown(object sender, TouchEventArgs e)
    {
        if (_activeSource != PointerSource.None)
        {
            // 已有鼠标或触摸在驱动，忽略额外触点。
            e.Handled = true;
            return;
        }

        _activeTouchId = e.TouchDevice.Id;
        BeginPointerInteraction(PointerSource.Touch, PointToScreen(e.GetTouchPoint(this).Position));

        if (!RootGrid.CaptureTouch(e.TouchDevice))
        {
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

    /// <summary>触摸抬起：拖拽过则钳制保存；未拖动的轻触不做任何动作。</summary>
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

        e.Handled = true;
    }

    /// <summary>左键单击（未拖动，<4px）打开径向快捷菜单。</summary>
    private void OpenRadialMenu()
    {
        _radialMenu?.Close();

        // 每次打开都重新读取配置：外部修改 shortcuts.json 后无需重启；也排除旧实例的缓存。
        _shortcutStore = ShortcutStore.Load();

        Point ballCenter = PointToScreen(new Point(Width / 2, Height / 2));
        var radial = new RadialMenuWindow(_shortcutStore, ballCenter, this);
        _radialMenu = radial;
        radial.Closed += (_, _) =>
        {
            if (ReferenceEquals(_radialMenu, radial))
            {
                _radialMenu = null;
            }
        };
        radial.Show();
    }

    /// <summary>
    /// 把悬浮球移回主屏工作区中央并立即落盘；仅由右键菜单/托盘菜单的「重置位置」调用。
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

    /// <summary>显示「关于」对话框；右键菜单与托盘菜单共用。</summary>
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
