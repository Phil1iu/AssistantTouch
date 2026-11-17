using System.ComponentModel;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace AssistantTouch;

/// <summary>
/// 桌面悬浮球主窗口：无边框、置顶、半透明、可鼠标/触摸拖拽，并记忆上次位置。
/// 拖到工作区边缘松手会自动贴边收起（只留 16px 入口），鼠标移上去展开、移开 300ms 后再次收起。
/// 左键口径：未拖动松开 = 打开径向菜单（鼠标与 UIA Invoke 等效），拖动 &gt;4px = 移动球；
/// 触摸轻触只展开/不动作；右键菜单与托盘菜单提供重置、自启、关于、退出。
/// </summary>
public partial class MainWindow : Window
{
    /// <summary>超过该设备像素位移才进入拖拽。</summary>
    private const double DragThresholdPixels = 4;

    /// <summary>贴边收起/展开的滑动动画时长（毫秒）。</summary>
    private const double DockAnimationMilliseconds = 150;

    /// <summary>鼠标离开展开后的球，等待多久再收起（毫秒）；期间回来则取消。</summary>
    private const int DockCollapseDelayMilliseconds = 300;

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

    /// <summary>当前贴边状态；<see cref="DockEdge.None"/> 表示未贴边。</summary>
    private DockEdge _dockEdge = DockEdge.None;

    /// <summary>贴边时是否处于收起态（只露 16px）；展开后仍保留 <see cref="_dockEdge"/>，鼠标离开会再次收起。</summary>
    private bool _isDockedCollapsed;

    /// <summary>贴边时球「展开后」应在的工作区内位置：收起坐标由它 + 边 + 工作区推出，落盘也只存它。</summary>
    private Point _expandedPosition;

    /// <summary>贴边时所在的显示器工作区（DIU）。收起态窗口中心在工作区外，不能再用中心点反查显示器。</summary>
    private Rect _dockWorkArea;

    /// <summary>鼠标离开后的收起定时器（按 300ms 周期复查，条件不满足就继续等）。</summary>
    private DispatcherTimer? _dockTimer;

    public MainWindow()
    {
        InitializeComponent();

        // 磨砂玻璃雾面噪点：程序生成的静态 TileBrush（XAML 里通过 DynamicResource 引用）。
        Resources["GlassNoiseBrush"] = FrostedGlassBrush.NoiseBrush;

        _shortcutStore = ShortcutStore.Load();
        Loaded += OnWindowLoaded;
        PreviewKeyDown += OnWindowPreviewKeyDown;
        RestoreSavedPosition();

        // UIA Invoke 等价「未拖动的左键单击」：打开径向菜单。
        // 环已打开时直接忽略：重复 Invoke 若走 OpenRadialMenu 会先 Close 再重建窗口，
        // 造成可见闪烁并丢失环内键盘选中态（读屏双击激活时尤其明显）。
        // 鼠标路径不经此处（OnMouseLeftButtonUp 直接调 OpenRadialMenu），两条路径行为一致。
        // 收起态下两条路径都会先展开再取圆心（OpenRadialMenu 内处理），避免环被屏幕边缘裁掉。
        RootGrid.TryInvoke = () =>
        {
            if (_radialMenu is not null)
            {
                return false;
            }

            OpenRadialMenu();
            return true;
        };
    }

    /// <summary>径向菜单打开时，Esc 也可由仍持有焦点的球窗口关闭。</summary>
    private void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_radialMenu is not null && e.Key == Key.Escape)
        {
            _radialMenu.CloseRing();
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
    /// 贴边状态只记下来，等 Loaded（拿到最终 DPI/工作区）再摆到收起位置。
    /// </remarks>
    private void RestoreSavedPosition()
    {
        if (!WindowPositionStore.TryLoad(out double savedLeft, out double savedTop, out DockEdge savedEdge))
        {
            return;
        }

        WindowStartupLocation = WindowStartupLocation.Manual;
        Point clamped = ClampToNearestMonitor(savedLeft, savedTop);
        Left = clamped.X;
        Top = clamped.Y;

        _dockEdge = savedEdge;
        _expandedPosition = clamped;

        // 启动钳制后若值变化，立即回写：避免强杀后仍留着越界的旧值。
        if (clamped.X != savedLeft || clamped.Y != savedTop)
        {
            SavePosition();
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
            // 一律走 SavePosition()：贴边态下必须存展开位置 + 边，不能把屏外收起坐标当普通位置存（rev-24 F1）。
            SavePosition();
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
        MonitorLayout.MonitorTarget target = GetNearestTarget(desiredLeft, desiredTop, out DpiScale currentDpi);
        double targetLeft = desiredLeft * currentDpi.DpiScaleX / target.Dpi.DpiScaleX;
        double targetTop = desiredTop * currentDpi.DpiScaleY / target.Dpi.DpiScaleY;
        return WindowClamp.Clamp(targetLeft, targetTop, Width, Height, target.WorkArea);
    }

    /// <summary>按窗口左上角（DIU）选最近显示器，返回该显示器的工作区与其 DPI。</summary>
    private MonitorLayout.MonitorTarget GetNearestTarget(double left, double top, out DpiScale currentDpi)
    {
        currentDpi = VisualTreeHelper.GetDpi(this);

        double physicalLeft = left * currentDpi.DpiScaleX;
        double physicalTop = top * currentDpi.DpiScaleY;
        double physicalCenterX = physicalLeft + Width * currentDpi.DpiScaleX / 2;
        double physicalCenterY = physicalTop + Height * currentDpi.DpiScaleY / 2;

        return MonitorLayout.GetNearestTarget(physicalCenterX, physicalCenterY, currentDpi);
    }

    private MonitorLayout.MonitorTarget GetNearestTarget(Point position, out DpiScale currentDpi)
    {
        return GetNearestTarget(position.X, position.Y, out currentDpi);
    }

    /// <summary>
    /// 贴边到指定边：记下展开位置与所在工作区，落盘，再滑到收起位置。
    /// </summary>
    /// <remarks>
    /// 两个调用来源：
    /// 拖拽松手贴边（此时 <see cref="_dockEdge"/> 已被 <see cref="ClearDock"/> 清空）用当前窗口位置作参考位置；
    /// 已经贴边、鼠标离开后再次收起时沿用已知的展开位置——不能取当前窗口位置，
    /// 鼠标离开得早时展开动画可能还没走完，读到的会是动画中途的坐标。
    /// 最后把参考位置归一化为「按工作区边缘重算的展开位置」（贴边轴贴齐）：松手点可能距边缘 ≤8px，
    /// 归一化后落盘的坐标与悬停展开后的 Left/Top 逐像素一致，重复调用幂等（rev-24 O1 / D108）。
    /// </remarks>
    private void DockTo(DockEdge edge, bool animate)
    {
        CommitAnimation();

        if (_dockEdge == DockEdge.None)
        {
            _expandedPosition = new Point(Left, Top);
            _dockWorkArea = GetNearestTarget(_expandedPosition, out _).WorkArea;
        }

        _dockEdge = edge;
        _expandedPosition = ComputeExpandedPosition();

        _isDockedCollapsed = true;
        StopDockTimer();

        SavePosition();
        MoveTo(ComputeDockedPosition(), animate);
    }

    /// <summary>从收起态展开（鼠标悬停 / 触摸按下 / 直接打开径向菜单）。</summary>
    private void ExpandFromDock(bool animate)
    {
        if (_dockEdge == DockEdge.None)
        {
            return;
        }

        _isDockedCollapsed = false;
        MoveTo(ComputeExpandedPosition(), animate);
    }

    /// <summary>解除贴边（开始拖动 / 重置位置）：窗口留在当前位置，之后按普通钳制处理。</summary>
    private void ClearDock()
    {
        if (_dockEdge == DockEdge.None)
        {
            return;
        }

        _dockEdge = DockEdge.None;
        _isDockedCollapsed = false;
        StopDockTimer();
    }

    private Point ComputeDockedPosition()
    {
        return EdgeDocking.ComputeDockedPosition(
            _dockEdge, _expandedPosition.X, _expandedPosition.Y, Width, Height, _dockWorkArea, EdgeDocking.DockVisiblePx);
    }

    private Point ComputeExpandedPosition()
    {
        return EdgeDocking.ComputeExpandedPosition(
            _dockEdge, _expandedPosition.X, _expandedPosition.Y, Width, Height, _dockWorkArea, EdgeDocking.ExpandMarginPx);
    }

    /// <summary>
    /// 唯一的落盘入口：未贴边存当前位置；贴边一律存「展开态」坐标 + 边（收起坐标在工作区外，重启后按边重算）。
    /// </summary>
    /// <remarks>
    /// 所有保存路径都必须经过这里——直接调 <see cref="WindowPositionStore"/> 会绕过贴边口径（rev-24 F1）。
    /// </remarks>
    private void SavePosition()
    {
        WindowPositionStore.Save(WindowPositionStore.ComposeSavedPosition(
            _dockEdge, _expandedPosition.X, _expandedPosition.Y, Left, Top));
    }

    /// <summary>鼠标进入球（收起态下只剩 16px 入口）：停止收起并展开。</summary>
    private void OnRootMouseEnter(object sender, MouseEventArgs e)
    {
        StopDockTimer();

        if (_dockEdge == DockEdge.None || !_isDockedCollapsed)
        {
            return;
        }

        ExpandFromDock(animate: true);
    }

    /// <summary>鼠标离开球：300ms 后如果还没回来就收起（贴边态才有意义）。</summary>
    private void OnRootMouseLeave(object sender, MouseEventArgs e)
    {
        StartDockTimer();
    }

    /// <summary>
    /// 开始 300ms 收起等待。只在「已贴边且已展开」时生效，重复调用无副作用。
    /// </summary>
    private void StartDockTimer()
    {
        if (_dockEdge == DockEdge.None || _isDockedCollapsed)
        {
            return;
        }

        if (_dockTimer is null)
        {
            _dockTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(DockCollapseDelayMilliseconds) };
            _dockTimer.Tick += OnDockTimerTick;
        }

        _dockTimer.Start();
    }

    private void StopDockTimer()
    {
        _dockTimer?.Stop();
    }

    /// <summary>
    /// 收起判定。按 300ms 周期复查而不是「离开时一次性判断」：
    /// 离开球以后窗口不再收到鼠标事件（径向菜单/右键菜单打开期间尤其明显），
    /// 只有定时复查才能在条件消失（菜单关闭、鼠标真的走了）之后自然收起。
    /// </summary>
    private void OnDockTimerTick(object? sender, EventArgs e)
    {
        if (_dockEdge == DockEdge.None || _isDockedCollapsed)
        {
            StopDockTimer();
            return;
        }

        // 光标还在贴边区域（展开球 ∪ 收起条带的几何并集）、仍在球上、正在拖动、或菜单开着：继续等下一轮。
        // 不能只靠 IsMouseOver：收起条带伸出工作区那一段（任务栏/相邻显示器/取整误差）命中收起球，
        // 却落在展开球之外，展开后必然收到 Leave，形成「展开→收起→展开」的 0.3–0.6s 振荡（D110 闪烁）。
        if (IsCursorInDockRegion() || IsMouseOver || _isPointerDown || _radialMenu is not null || IsContextMenuOpen())
        {
            return;
        }

        DockTo(_dockEdge, animate: true);
    }

    /// <summary>
    /// 光标是否仍在贴边区域：展开窗口矩形与收起条带矩形的并集（含 <see cref="DockHover.TolerancePx"/> 容差）。
    /// 这是收起判定与展开兜底的唯一几何口径；<c>IsMouseOver</c> 只作补充。
    /// </summary>
    private bool IsCursorInDockRegion()
    {
        if (_dockEdge == DockEdge.None || !TryGetCursorScreenDiu(out Point cursor))
        {
            return false;
        }

        var size = new Size(Width, Height);

        return DockHover.IsCursorInDockRegion(
            cursor,
            new Rect(ComputeExpandedPosition(), size),
            new Rect(ComputeDockedPosition(), size),
            DockHover.TolerancePx);
    }

    /// <summary>
    /// 光标在虚拟屏幕上的位置（DIU）：窗口左上角当前（含动画中途）的设备坐标与 <see cref="Window.Left"/>/<see cref="Window.Top"/>（DIU）
    /// 配对换算，避免把设备像素与 DIU 混在一起比较；混合 DPI 下也不依赖主屏缩放。
    /// </summary>
    private bool TryGetCursorScreenDiu(out Point point)
    {
        point = default;

        // 球被托盘隐藏/窗口尚未挂到 PresentationSource 时没有可靠的窗口原点，取不到就交给 IsMouseOver 分支。
        if (!IsVisible || PresentationSource.FromVisual(this) is null)
        {
            return false;
        }

        if (!CursorProbe.TryGetScreenPosition(out Point deviceCursor))
        {
            return false;
        }

        Point originDevice = PointToScreen(new Point(0, 0));
        DpiScale dpi = VisualTreeHelper.GetDpi(this);

        if (dpi.DpiScaleX <= 0 || dpi.DpiScaleY <= 0)
        {
            return false;
        }

        point = new Point(
            Left + (deviceCursor.X - originDevice.X) / dpi.DpiScaleX,
            Top + (deviceCursor.Y - originDevice.Y) / dpi.DpiScaleY);
        return true;
    }

    /// <summary>
    /// 展开的几何兜底：收起态下光标已在贴边区域内（例如窗口收起后滑回光标下方，MouseEnter 已发过、不再重发），
    /// 鼠标一移动就展开，不用等下一次 MouseEnter。
    /// </summary>
    private void TryExpandFromDockOnHover()
    {
        if (_dockEdge == DockEdge.None || !_isDockedCollapsed || _isPointerDown)
        {
            return;
        }

        if (IsCursorInDockRegion())
        {
            ExpandFromDock(animate: true);
        }
    }

    private bool IsContextMenuOpen()
    {
        return RootGrid.ContextMenu?.IsOpen == true;
    }

    /// <summary>平移窗口到指定左上角；<paramref name="animate"/> 为 true 时用 150ms 缓出滑动。</summary>
    private void MoveTo(Point position, bool animate)
    {
        CommitAnimation();

        if (!animate)
        {
            Left = position.X;
            Top = position.Y;
            return;
        }

        TimeSpan duration = TimeSpan.FromMilliseconds(DockAnimationMilliseconds);
        BeginSlideAnimation(LeftProperty, position.X, duration);
        BeginSlideAnimation(TopProperty, position.Y, duration);
    }

    /// <summary>
    /// 停止进行中的滑动动画，并把窗口钉在动画当前所在位置。
    /// </summary>
    /// <remarks>
    /// 动画用 <see cref="FillBehavior.HoldEnd"/>：直接 <c>BeginAnimation(prop, null)</c> 会跳回基础值（动画开始前的位置），
    /// 拖拽时的「按下位置 + 位移」就会整体错位。先写基础值（动画仍生效、视觉不变）再撤动画，窗口停在当前处。
    /// </remarks>
    private void CommitAnimation()
    {
        double currentLeft = Left;
        double currentTop = Top;

        Left = currentLeft;
        Top = currentTop;
        BeginAnimation(LeftProperty, null);
        BeginAnimation(TopProperty, null);
    }

    private void BeginSlideAnimation(DependencyProperty property, double to, TimeSpan duration)
    {
        var animation = new DoubleAnimation(to, duration)
        {
            FillBehavior = FillBehavior.HoldEnd,
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };

        animation.Completed += (_, _) =>
        {
            if (!IsLoaded)
            {
                return;
            }

            // 定格收尾：把基础值落到目标位置再撤动画，位置不回弹，也不留下 HeldEnd 的动画对象。
            SetValue(property, to);
            BeginAnimation(property, null);
        };

        BeginAnimation(property, animation);
    }

    /// <summary>关闭径向菜单（托盘隐藏、窗口关闭时调用）。</summary>
    internal void CloseRadialMenu()
    {
        if (_radialMenu is not null)
        {
            _radialMenu.CloseRing();
            _radialMenu = null;
        }
    }

    /// <summary>正常关闭窗口时保存当前位置（位置文件写失败不影响退出）。</summary>
    protected override void OnClosing(CancelEventArgs e)
    {
        CloseRadialMenu();
        StopDockTimer();
        SavePosition();
        base.OnClosing(e);
    }

    /// <summary>首次显示后，用最终（可能是目标显示器）的 DPI 再钳制一次并保存；贴边态则摆回收起位置。</summary>
    private void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (_dockEdge != DockEdge.None)
        {
            _dockWorkArea = GetNearestTarget(_expandedPosition, out _).WorkArea;
            _expandedPosition = ComputeExpandedPosition(); // O1：旧位置文件可能带 ≤8px 的松手点偏差，恢复时归一化
            _isDockedCollapsed = true;
            MoveTo(ComputeDockedPosition(), animate: false);
            SavePosition();
            return;
        }

        ClampBallToNearestMonitorAndSaveIfMoved();
    }

    /// <summary>
    /// 跨显示器导致 DPI 变化：按新 DPI 重新计算工作区并钳制；拖拽中重置指针/窗口基准，避免跳变。
    /// 贴边态按新 DPI 重算工作区，收起/展开哪个态就摆到哪个态。
    /// </summary>
    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);

        if (_isPointerDown)
        {
            ClampBallToNearestMonitor();
            _pointerDownScreen = _lastPointerScreen;
            _windowAtPointerDown = new Point(Left, Top);
            return;
        }

        if (_dockEdge != DockEdge.None)
        {
            CommitAnimation();
            _dockWorkArea = GetNearestTarget(_expandedPosition, out _).WorkArea;
            _expandedPosition = ComputeExpandedPosition(); // O1：按新工作区归一化，落盘值始终等于展开位
            MoveTo(_isDockedCollapsed ? ComputeDockedPosition() : ComputeExpandedPosition(), animate: false);
            SavePosition();
            return;
        }

        ClampBallToNearestMonitorAndSaveIfMoved();
    }

    /// <summary>鼠标/触摸共用的按下处理：记录起点与窗口位置，标记当前输入源。</summary>
    private void BeginPointerInteraction(PointerSource source, Point screenPoint)
    {
        // 先把滑动动画定格：否则下面记下的窗口基准是动画中途的位置，松手会跳。
        CommitAnimation();

        // 触摸没有悬停：直接按在收起态入口上时立即展开，之后的轻触/拖动都以完整球为基准。
        if (_dockEdge != DockEdge.None && _isDockedCollapsed)
        {
            ExpandFromDock(animate: false);
        }

        StopDockTimer();

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
            // 拖离边缘即解除贴边（口径 6）：之后按普通拖动钳制，松手时再重新判定贴哪条边。
            ClearDock();

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

    /// <summary>
    /// 拖拽结束后：钳制到最近显示器工作区；球边距工作区边缘 ≤ 8px 时自动贴边收起，否则按普通位置落盘。
    /// </summary>
    private void FinishDrag()
    {
        ClampBallToNearestMonitor();

        MonitorLayout.MonitorTarget target = GetNearestTarget(Left, Top, out _);
        DockEdge edge = EdgeDocking.DetectEdge(Left, Top, Width, Height, target.WorkArea, EdgeDocking.SnapThresholdPx);

        if (edge == DockEdge.None)
        {
            ClearDock();
            SavePosition();
            return;
        }

        DockTo(edge, animate: true);
    }

    /// <summary>
    /// 左键按下：准备拖拽（未拖动的松开在 <see cref="OnMouseLeftButtonUp"/> 里打开径向菜单）。
    /// 贴边收起态下按下会先展开，拖动/单击都以完整球为基准。
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
        // 收起态下鼠标在入口上移动：MouseEnter 漏发时也按几何兜底展开（D110）。
        TryExpandFromDockOnHover();

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
                StartDockTimer();
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

        // 贴边态：鼠标离开球（去点菜单/移走）后按 300ms 复查收起；鼠标还在球上时定时器空转一轮即继续等。
        StartDockTimer();

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

    /// <summary>
    /// 触摸按下：只接受第一个触点。触摸没有悬停，按在收起态入口上会先立即展开；
    /// 未拖动的轻触不打开径向菜单。
    /// </summary>
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

    /// <summary>
    /// 触摸抬起：拖拽过则钳制并可能贴边；轻触不打开径向菜单。
    /// 触摸没有 MouseLeave，贴边态下抬起后由 300ms 定时器复查收起（手指已离开时不展开）。
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

        // 触摸没有 MouseLeave：轻触后由定时器在 300ms 后（指针已不在球上）收起。
        StartDockTimer();

        e.Handled = true;
    }

    /// <summary>左键单击（未拖动，<4px）打开径向快捷菜单。</summary>
    private void OpenRadialMenu()
    {
        // 收起态直接开环会以半个球为圆心、环被屏幕边缘裁掉：先展开再取圆心。
        if (_isDockedCollapsed)
        {
            ExpandFromDock(animate: false);
        }

        // 统一走 CloseRing：先置 _closed 再关闭，阻止同一 tick 内旧环的迟到激活。
        _radialMenu?.CloseRing();

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

        // 环开着时不收起（定时器复查时会跳过），关环后由定时器按鼠标位置决定是否收起。
        StartDockTimer();
    }

    /// <summary>
    /// 把悬浮球移回主屏工作区中央并立即落盘；仅由右键菜单/托盘菜单的「重置位置」调用。
    /// </summary>
    internal void ResetToPrimaryCenter()
    {
        // 先关闭径向环，避免全屏透明窗口在菜单/对话框期间残留抢输入。
        CloseRadialMenu();

        // 重置即解除贴边：窗口回到工作区中央。
        CommitAnimation();
        ClearDock();

        Rect workArea = SystemParameters.WorkArea;
        Left = workArea.Left + (workArea.Width - Width) / 2;
        Top = workArea.Top + (workArea.Height - Height) / 2;
        ClampBallToNearestMonitor();
        SavePosition();
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
        // 先关闭径向环，避免对话框打开期间全屏环仍在屏幕上接收输入。
        CloseRadialMenu();

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
        // 先关闭径向环，避免退出确认框期间全屏环仍在屏幕上接收输入。
        CloseRadialMenu();

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
