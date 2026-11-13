using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AssistantTouch;

/// <summary>
/// 左键单击大圆后出现的径向快捷菜单：全屏透明置顶窗口，周围均匀分布快捷小圆与「+」圆。
/// 点非圆区域或 Esc 关闭；点击小圆执行动作并关闭；右键小圆可删除/更换/恢复默认。
/// </summary>
public partial class RadialMenuWindow : Window
{
    private const double CircleSize = 44;
    private const double RingRadius = 95;
    private const double MinDistanceFromBall = 70;
    private const double DragStartThreshold = 6;

    private readonly ShortcutStore _store;
    private readonly Point _ballCenterScreen;
    private readonly Window _ownerWindow;
    private readonly List<CircleSlot> _circleSlots = new();

    private Point _dragStartPoint;
    private ShortcutDefinition? _dragShortcut;
    private Border? _dragCircle;
    private bool _dragStarted;
    private int _selectedIndex;
    private bool _selectionActive;

    /// <summary>当前环上实际渲染的圆（快捷项在前、「+」在末位；Partial 时可能只有「+」）。</summary>
    private sealed record CircleSlot(Border Circle, Border FocusRing, ShortcutDefinition? Shortcut, Point Center);

    internal RadialMenuWindow(ShortcutStore store, Point ballCenterScreen, Window owner)
    {
        InitializeComponent();

        _store = store;
        _ballCenterScreen = ballCenterScreen;
        _ownerWindow = owner;

        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;

        Loaded += OnLoaded;
        PreviewKeyDown += OnPreviewKeyDown;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        RebuildRing();

        // 确保径向窗口拿到键盘焦点，Esc 才能关闭。
        Activate();
        RingCanvas.Focus();
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
            return;
        }

        int? next = KeyboardNavigation.Next(_selectedIndex, _circleSlots.Count, e.Key);
        if (next is int index)
        {
            _selectedIndex = index;
            _selectionActive = true;
            ApplySelectionVisual();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter || e.Key == Key.Return || e.Key == Key.Space)
        {
            if (ActivateSelectedSlot())
            {
                e.Handled = true;
            }
        }
    }

    /// <summary>执行当前选中项：快捷项执行并关环，「+」打开添加对话框。</summary>
    private bool ActivateSelectedSlot()
    {
        if (!_selectionActive || _selectedIndex < 0 || _selectedIndex >= _circleSlots.Count)
        {
            return false;
        }

        CircleSlot slot = _circleSlots[_selectedIndex];
        if (slot.Shortcut is null)
        {
            ShowAddShortcutMenu(slot.Circle, PlacementMode.Bottom, includeRestoreDefaults: false);
        }
        else
        {
            ShortcutActions.Execute(slot.Shortcut);
            Close();
        }

        return true;
    }

    private void OnCanvasMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        // 点非圆区域（包括再次点击大圆位置）关闭。
        Close();
    }

    private void OnCanvasMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        // 环内空白处右键：不论项数，都提供系统功能/应用程序添加与「恢复默认…」。
        ShowAddShortcutMenu(RingCanvas, PlacementMode.MousePoint, includeRestoreDefaults: true);
        e.Handled = true;
    }

    /// <summary>
    /// 「+」左键/键盘（includeRestoreDefaults=false）与环背景/「+」右键（true）共用的添加菜单。
    /// 已添加的系统项置灰并标「已添加」；满 8 个时全部系统项禁用并显示上限提示。
    /// </summary>
    private void ShowAddShortcutMenu(UIElement target, PlacementMode placement, bool includeRestoreDefaults)
    {
        var menu = new ContextMenu();
        bool atLimit = _store.Shortcuts.Count >= ShortcutStore.MaxShortcuts;

        if (atLimit)
        {
            menu.Items.Add(new MenuItem
            {
                Header = "已达上限（8 个）",
                IsEnabled = false
            });
        }

        foreach (SystemShortcut entry in SystemShortcutCatalog.All)
        {
            bool added = SystemShortcutCatalog.ContainsSystemAction(_store.Shortcuts, entry.Action);

            var item = new MenuItem
            {
                Header = added ? $"{entry.Name}（已添加）" : entry.Name,
                IsEnabled = !atLimit && !added
            };

            if (atLimit)
            {
                item.ToolTip = "已达上限（8 个）";
                ToolTipService.SetShowOnDisabled(item, true);
            }
            else if (added)
            {
                item.ToolTip = "已添加";
                ToolTipService.SetShowOnDisabled(item, true);
            }

            SystemShortcut captured = entry;
            item.Click += (_, _) => AddSystemShortcut(captured);
            menu.Items.Add(item);
        }

        menu.Items.Add(new Separator());

        var addApplicationItem = new MenuItem { Header = "添加应用程序…" };
        addApplicationItem.Click += (_, _) => OnAddApplicationClick();
        menu.Items.Add(addApplicationItem);

        if (includeRestoreDefaults)
        {
            menu.Items.Add(new Separator());
            var restoreItem = new MenuItem { Header = "恢复默认…" };
            restoreItem.Click += (_, _) => RestoreDefaultsWithConfirmation();
            menu.Items.Add(restoreItem);
        }

        // 菜单关闭后把键盘焦点交还环；若环已隐藏（如打开了文件对话框），不要抢焦点。
        menu.Closed += (_, _) =>
        {
            if (IsVisible)
            {
                FocusRingCanvas();
            }
        };

        menu.PlacementTarget = target;
        menu.Placement = placement;
        menu.IsOpen = true;
    }

    private void AddSystemShortcut(SystemShortcut entry)
    {
        SystemShortcutAddDecision decision = SystemShortcutCatalog.EvaluateAdd(_store.Shortcuts, entry.Action);

        switch (decision)
        {
            case SystemShortcutAddDecision.Allowed:
                var shortcut = new ShortcutDefinition
                {
                    Kind = ShortcutKind.System,
                    Action = entry.Action,
                    Name = entry.Name
                };

                if (_store.TryAdd(shortcut))
                {
                    // 环保持打开，便于连续添加；重排后焦点回到环上。
                    RebuildRing();
                    FocusRingCanvas();
                }
                else
                {
                    ShowAddLimitMessage();
                }

                break;

            case SystemShortcutAddDecision.AlreadyAdded:
                MessageBox.Show(
                    this,
                    "该快捷方式已添加",
                    "AssistantTouch",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                FocusRingCanvas();
                break;

            case SystemShortcutAddDecision.LimitReached:
                ShowAddLimitMessage();
                break;

            case SystemShortcutAddDecision.UnknownAction:
            default:
                break;
        }
    }

    private void ShowAddLimitMessage()
    {
        MessageBox.Show(
            this,
            "最多 8 个快捷启动",
            "AssistantTouch",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void RebuildRing()
    {
        // P3-3：重建前记录当前选中项（按引用），重建后跟随它；未激活选中态时只 clamp 索引。
        ShortcutDefinition? previousItem = null;
        if (_selectionActive && _selectedIndex >= 0 && _selectedIndex < _circleSlots.Count)
        {
            previousItem = _circleSlots[_selectedIndex].Shortcut;
        }

        int previousIndex = _selectedIndex;

        RingCanvas.Children.Clear();
        _circleSlots.Clear();

        int shortcutCount = _store.Shortcuts.Count;
        int total = shortcutCount + 1; // + 「+」圆

        Point ballCenter = PointFromScreen(_ballCenterScreen);
        Rect workArea = GetWorkAreaClientRect(_ballCenterScreen);

        // 几何全部交给纯函数：中部保持旧整圆；贴边/贴角自动收拢为单弧或内外双弧，
        // 保证所有快捷项 +「+」都在当前显示器工作区内、互不重叠且不压大球。
        RadialLayoutResult layout = RadialLayout.Compute(
            ballCenter,
            workArea,
            total,
            CircleSize,
            RingRadius,
            MinDistanceFromBall);

        // Partial（极小/异常工作区）按 rev-10 P2 降级：不渲染任何快捷小圆，只尝试画「+」。
        if (layout.AllPlaced)
        {
            for (int i = 0; i < shortcutCount && i < layout.Centers.Count; i++)
            {
                ShortcutDefinition shortcut = _store.Shortcuts[i];
                Point center = layout.Centers[i];
                Border circle = CreateShortcutCircle(shortcut);
                AddCircle(circle, center);
                _circleSlots.Add(new CircleSlot(circle, GetFocusRing(circle), shortcut, center));
            }
        }

        Point? plusPosition = layout.AllPlaced && layout.Centers.Count >= total
            ? layout.Centers[shortcutCount]
            : RadialLayout.ChooseFallbackCenter(
                ballCenter,
                workArea,
                CircleSize,
                MinDistanceFromBall,
                layout.Centers,
                out _);

        // 兜底纯函数确认无合法位置（null）时不画「+」，绝不让它压在球心上。
        if (plusPosition is { } plusPoint)
        {
            Border plusCircle = CreatePlusCircle();
            AddCircle(plusCircle, plusPoint);
            _circleSlots.Add(new CircleSlot(plusCircle, GetFocusRing(plusCircle), null, plusPoint));
        }

        List<ShortcutDefinition?> slots = _circleSlots.Select(slot => slot.Shortcut).ToList();
        _selectedIndex = SelectionResolver.Resolve(_selectionActive, previousIndex, previousItem, slots);
        if (_selectedIndex < 0)
        {
            _selectionActive = false;
        }

        ApplySelectionVisual();
    }

    private static Border GetFocusRing(Border circle) => (Border)circle.Tag;

    private void ApplySelectionVisual()
    {
        for (int i = 0; i < _circleSlots.Count; i++)
        {
            _circleSlots[i].FocusRing.Visibility =
                _selectionActive && i == _selectedIndex ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    /// <summary>把圆排到指定中心；位置由 <see cref="RadialLayout"/> 纯函数给出。</summary>
    private void AddCircle(Border circle, Point center)
    {
        Canvas.SetLeft(circle, center.X - CircleSize / 2);
        Canvas.SetTop(circle, center.Y - CircleSize / 2);
        RingCanvas.Children.Add(circle);
    }

    private Rect GetWorkAreaClientRect(Point screenPoint)
    {
        System.Drawing.Point devicePoint = new((int)screenPoint.X, (int)screenPoint.Y);
        System.Drawing.Rectangle work = System.Windows.Forms.Screen.FromPoint(devicePoint).WorkingArea;

        Point topLeft = PointFromScreen(new Point(work.Left, work.Top));
        Point bottomRight = PointFromScreen(new Point(work.Right, work.Bottom));
        return new Rect(topLeft, bottomRight);
    }

    private Border CreateShortcutCircle(ShortcutDefinition shortcut)
    {
        Border circle = CreateCircleBase(shortcut.Name, CreateCircleContent(shortcut));

        // 左键：未拖动（<6px）执行动作；拖动到另一小圆中心半径内交换顺序。
        circle.MouseLeftButtonDown += (_, args) => BeginShortcutDrag(shortcut, circle, args);
        circle.MouseMove += (_, args) => ContinueShortcutDrag(args);
        circle.MouseLeftButtonUp += (_, args) => EndShortcutDrag(circle, args);
        circle.LostMouseCapture += (_, _) => CancelShortcutDrag(releaseCapture: false);

        circle.MouseRightButtonUp += (_, args) =>
        {
            args.Handled = true;
            ShowCircleMenu(circle, shortcut);
        };

        return circle;
    }

    private void BeginShortcutDrag(ShortcutDefinition shortcut, Border circle, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        CancelShortcutDrag();
        _dragShortcut = shortcut;
        _dragCircle = circle;
        _dragStartPoint = e.GetPosition(RingCanvas);
        _dragStarted = false;
        circle.CaptureMouse();
        e.Handled = true;
    }

    private void ContinueShortcutDrag(MouseEventArgs e)
    {
        if (_dragShortcut is null || _dragCircle is null)
        {
            return;
        }

        if (!_dragStarted)
        {
            if ((e.GetPosition(RingCanvas) - _dragStartPoint).Length < DragStartThreshold)
            {
                return;
            }

            _dragStarted = true;
            _dragCircle.Opacity = 0.7;
        }

        e.Handled = true;
    }

    private void EndShortcutDrag(Border circle, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || _dragShortcut is null)
        {
            return;
        }

        bool wasDragging = _dragStarted;
        ShortcutDefinition dragged = _dragShortcut;
        Point dropPoint = e.GetPosition(RingCanvas);

        CancelShortcutDrag();

        e.Handled = true;

        if (!wasDragging)
        {
            // 未超过阈值：保持原有「点击执行并关闭」行为。
            ShortcutActions.Execute(dragged);
            Close();
            return;
        }

        // 拖拽态：不执行动作；落在另一小圆中心半径内则交换，否则取消。
        ShortcutDefinition? target = FindDropTarget(dropPoint, dragged);
        if (target is not null && _store.Swap(dragged, target))
        {
            RebuildRing();
            FocusRingCanvas();
        }
    }

    private ShortcutDefinition? FindDropTarget(Point point, ShortcutDefinition dragged)
    {
        ShortcutDefinition? best = null;
        double bestDistance = CircleSize / 2;

        foreach (CircleSlot slot in _circleSlots)
        {
            if (slot.Shortcut is null || ReferenceEquals(slot.Shortcut, dragged))
            {
                continue;
            }

            double distance = (slot.Center - point).Length;
            if (distance <= bestDistance)
            {
                bestDistance = distance;
                best = slot.Shortcut;
            }
        }

        return best;
    }

    private void CancelShortcutDrag(bool releaseCapture = true)
    {
        if (_dragCircle is not null)
        {
            _dragCircle.Opacity = 1.0;
            if (releaseCapture && _dragCircle.IsMouseCaptured)
            {
                _dragCircle.ReleaseMouseCapture();
            }
        }

        _dragShortcut = null;
        _dragCircle = null;
        _dragStarted = false;
    }

    private Border CreatePlusCircle()
    {
        Border circle = CreateCircleBase("添加快捷启动", CreateLabel("+", 18));

        circle.MouseLeftButtonUp += (_, args) =>
        {
            args.Handled = true;
            ShowAddShortcutMenu(circle, PlacementMode.Bottom, includeRestoreDefaults: false);
        };

        circle.MouseRightButtonUp += (_, args) =>
        {
            // 与环背景一致：添加系统功能/应用程序 + 恢复默认（覆盖 0 项时的旧入口）。
            args.Handled = true;
            ShowAddShortcutMenu(circle, PlacementMode.Bottom, includeRestoreDefaults: true);
        };

        return circle;
    }

    private static Border CreateCircleBase(string tooltip, UIElement content)
    {
        // 选中态焦点描边：叠在内容之上、不参与命中测试，未选中时折叠。
        var focusRing = new Border
        {
            CornerRadius = new CornerRadius((CircleSize - 2) / 2),
            BorderThickness = new Thickness(2),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x00, 0x78, 0xD4)),
            Margin = new Thickness(1),
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed
        };

        var contentHost = new Grid();
        contentHost.Children.Add(content);
        contentHost.Children.Add(focusRing);

        var circle = new Border
        {
            Width = CircleSize,
            Height = CircleSize,
            CornerRadius = new CornerRadius(CircleSize / 2),
            ToolTip = tooltip,
            Cursor = Cursors.Hand,
            Child = contentHost,
            Tag = focusRing
        };

        // 小圆本身通常没有 automation peer；名称同时挂到 Border 和图标上，
        // 这样只有图标、没有文字的 app 圆也能被 UIA 按名字数到。
        // 文字圆保持 TextBlock 自己的文本名（如「桌面」），不被完整名称覆盖。
        AutomationProperties.SetName(circle, tooltip);
        if (content is Image)
        {
            AutomationProperties.SetName(content, tooltip);
        }

        var style = new Style(typeof(Border));
        style.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(Color.FromArgb(0xF5, 255, 255, 255))));
        style.Setters.Add(new Setter(Border.BorderBrushProperty, new SolidColorBrush(Color.FromArgb(0x80, 0, 0, 0))));
        style.Setters.Add(new Setter(Border.BorderThicknessProperty, new Thickness(1)));
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Border.BorderBrushProperty, new SolidColorBrush(Color.FromArgb(0xA6, 0, 0, 0))));
        style.Triggers.Add(hover);
        circle.Style = style;

        return circle;
    }

    private static UIElement CreateCircleContent(ShortcutDefinition shortcut)
    {
        if (shortcut.Kind == ShortcutKind.App)
        {
            ImageSource? icon = TryLoadIcon(shortcut.Path);
            if (icon is not null)
            {
                return new Image
                {
                    Source = icon,
                    Width = 24,
                    Height = 24,
                    IsHitTestVisible = false
                };
            }
        }

        return CreateLabel(GetShortLabel(shortcut), 13);
    }

    private static TextBlock CreateLabel(string text, double fontSize)
    {
        return new TextBlock
        {
            Text = text,
            FontSize = fontSize,
            Foreground = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false
        };
    }

    private static ImageSource? TryLoadIcon(string path)
    {
        try
        {
            using System.Drawing.Icon? icon = System.Drawing.Icon.ExtractAssociatedIcon(path);
            if (icon is null)
            {
                return null;
            }

            return Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
        }
        catch (Exception)
        {
            return null;
        }
    }

    private void ShowCircleMenu(Border circle, ShortcutDefinition shortcut)
    {
        var menu = new ContextMenu();
        int index = _store.IndexOf(shortcut);

        var renameItem = new MenuItem { Header = "重命名…" };
        renameItem.Click += (_, _) => RenameShortcut(shortcut);

        var moveCounterClockwiseItem = new MenuItem
        {
            Header = "逆时针移一位",
            IsEnabled = index > 0,
            ToolTip = "按存储顺序与前一位交换（整圆从正上方顺时针编号；前一位即逆时针相邻槽位）"
        };
        moveCounterClockwiseItem.Click += (_, _) =>
        {
            if (_store.Move(shortcut, -1))
            {
                RebuildRing();
                FocusRingCanvas();
            }
        };

        var moveClockwiseItem = new MenuItem
        {
            Header = "顺时针移一位",
            IsEnabled = index >= 0 && index < _store.Shortcuts.Count - 1,
            ToolTip = "按存储顺序与后一位交换（顺时针相邻槽位）"
        };
        moveClockwiseItem.Click += (_, _) =>
        {
            if (_store.Move(shortcut, 1))
            {
                RebuildRing();
                FocusRingCanvas();
            }
        };

        var deleteItem = new MenuItem { Header = "删除" };
        deleteItem.Click += (_, _) =>
        {
            _store.Remove(shortcut);
            RebuildRing();
            FocusRingCanvas();
        };

        var changeItem = new MenuItem { Header = "更换程序" };
        changeItem.Click += (_, _) => ReplaceShortcut(shortcut);

        var resetItem = new MenuItem { Header = "恢复默认" };
        resetItem.Click += (_, _) => RestoreDefaultsWithConfirmation();

        menu.Items.Add(renameItem);
        menu.Items.Add(moveCounterClockwiseItem);
        menu.Items.Add(moveClockwiseItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(deleteItem);
        menu.Items.Add(changeItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(resetItem);

        menu.PlacementTarget = circle;
        menu.IsOpen = true;
    }

    private void RenameShortcut(ShortcutDefinition shortcut)
    {
        // 沿用添加流程：先隐藏全屏置顶的环，再以悬浮球主窗口为 Owner 弹对话框。
        HideRadialForDialog();
        try
        {
            var dialog = new RenameShortcutWindow(shortcut.Name)
            {
                Owner = _ownerWindow
            };
            if (dialog.ShowDialog() == true)
            {
                _store.Rename(shortcut, dialog.ResultName);
            }
        }
        finally
        {
            ShowRadialAfterDialog();
        }
    }

    /// <summary>
    /// 两个「恢复默认」入口共用的二次确认：先关环、弹确认框（默认焦点在取消），
    /// 只有点「恢复」才清除自定义并保存；取消/Esc/关闭不改数据。始终弹确认（即使已是默认）。
    /// </summary>
    private void RestoreDefaultsWithConfirmation()
    {
        HideRadialForDialog();
        try
        {
            var dialog = new ConfirmRestoreDefaultsWindow
            {
                Owner = _ownerWindow
            };

            RestoreDefaultsConfirmation.Apply(dialog.ShowDialog(), _store.ResetToDefaults);
        }
        finally
        {
            ShowRadialAfterDialog();
        }
    }

    private void FocusRingCanvas()
    {
        Activate();
        RingCanvas.Focus();
    }

    private void ReplaceShortcut(ShortcutDefinition shortcut)
    {
        HideRadialForDialog();
        try
        {
            string? path = PickApplication();
            if (path is null)
            {
                return;
            }

            var replacement = new ShortcutDefinition
            {
                Kind = ShortcutKind.App,
                Path = path,
                Name = Path.GetFileNameWithoutExtension(path)
            };

            _store.Replace(shortcut, replacement);
        }
        finally
        {
            ShowRadialAfterDialog();
        }
    }

    private void OnAddApplicationClick()
    {
        if (_store.Shortcuts.Count >= ShortcutStore.MaxShortcuts)
        {
            MessageBox.Show(this, "最多 8 个快捷启动", "AssistantTouch", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // 全屏置顶的径向窗口会挡住通用文件对话框（不可点击/无法聚焦）：
        // 先取消置顶并隐藏，对话框结束后再显示并重排。取消选择也重新打开环。
        HideRadialForDialog();
        try
        {
            string? path = PickApplication();
            if (path is null)
            {
                return;
            }

            var shortcut = new ShortcutDefinition
            {
                Kind = ShortcutKind.App,
                Path = path,
                Name = Path.GetFileNameWithoutExtension(path)
            };

            if (!_store.TryAdd(shortcut))
            {
                MessageBox.Show(
                    _ownerWindow,
                    "最多 8 个快捷启动",
                    "AssistantTouch",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }
        finally
        {
            ShowRadialAfterDialog();
        }
    }

    private void HideRadialForDialog()
    {
        Topmost = false;
        Hide();
    }

    private void ShowRadialAfterDialog()
    {
        // P3-1：对话框返回重开时重置键盘选中态，首次按方向键才显示焦点框。
        _selectionActive = false;
        _selectedIndex = 0;
        Show();
        Topmost = true;
        Activate();
        RingCanvas.Focus();
        RebuildRing();
    }

    private string? PickApplication()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择程序",
            Filter = "程序 (*.exe;*.lnk)|*.exe;*.lnk|所有文件 (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        // 文件对话框以悬浮球主窗口为 Owner（不用径向窗口），环已隐藏，确保对话框可聚焦、可键盘输入。
        bool? result = dialog.ShowDialog(_ownerWindow);

        return result == true ? dialog.FileName : null;
    }

    private static string GetShortLabel(ShortcutDefinition shortcut)
    {
        string name = shortcut.Name.Trim();

        if (shortcut.Kind == ShortcutKind.App)
        {
            string fileName = Path.GetFileNameWithoutExtension(name);
            return string.IsNullOrWhiteSpace(fileName) ? "应用" : Shorten(fileName);
        }

        // 默认系统名称保持原有 2 字标签（与历史 UI/自动化断言一致）；
        // 用户重命名后改用新名的前 2 个字符，使文字圆立即反映新名。
        string defaultName = shortcut.Action switch
        {
            "showDesktop" => "显示桌面",
            "screenshot" => "截图",
            "taskManager" => "任务管理器",
            "settings" => "设置",
            "explorer" => "文件资源管理器",
            "lock" => "锁定电脑",
            _ => string.Empty
        };

        if (string.Equals(name, defaultName, StringComparison.Ordinal))
        {
            return shortcut.Action switch
            {
                "showDesktop" => "桌面",
                "screenshot" => "截图",
                "taskManager" => "任务",
                "settings" => "设置",
                "explorer" => "文件",
                "lock" => "锁定",
                _ => "快捷"
            };
        }

        return Shorten(string.IsNullOrWhiteSpace(name) ? "快捷" : name);
    }

    private static string Shorten(string name)
    {
        return name[..Math.Min(2, name.Length)];
    }
}
