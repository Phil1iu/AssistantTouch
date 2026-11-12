using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
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

    private readonly ShortcutStore _store;
    private readonly Point _ballCenterScreen;
    private readonly Window _ownerWindow;

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
        }
    }

    private void OnCanvasMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        // 点非圆区域（包括再次点击大圆位置）关闭。
        Close();
    }

    private void OnCanvasMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        // 0 项（只剩「+」）时，右键环背景也能恢复默认。
        if (_store.Shortcuts.Count == 0)
        {
            ShowRestoreDefaultsMenu(RingCanvas);
            e.Handled = true;
        }
    }

    private void ShowRestoreDefaultsMenu(UIElement target)
    {
        var menu = new ContextMenu();
        var resetItem = new MenuItem { Header = "恢复默认" };
        resetItem.Click += (_, _) =>
        {
            _store.ResetToDefaults();
            RebuildRing();
        };

        menu.Items.Add(resetItem);
        menu.PlacementTarget = target;
        menu.IsOpen = true;
    }

    private void RebuildRing()
    {
        RingCanvas.Children.Clear();

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

        for (int i = 0; i < shortcutCount && i < layout.Centers.Count; i++)
        {
            AddCircle(CreateShortcutCircle(_store.Shortcuts[i]), layout.Centers[i]);
        }

        // 极端小工作区下几何可能放不下全部槽位（layout.AllPlaced=false）：
        // 仍把「+」画在工作区内的兜底位置，避免无法添加快捷项，也绝不抛异常。
        Point plusPosition = layout.Centers.Count >= total
            ? layout.Centers[shortcutCount]
            : GetFallbackPlusPosition(ballCenter, workArea);
        AddCircle(CreatePlusCircle(), plusPosition);
    }

    private void AddCircle(Border circle, Point center)
    {
        Canvas.SetLeft(circle, center.X - CircleSize / 2);
        Canvas.SetTop(circle, center.Y - CircleSize / 2);
        RingCanvas.Children.Add(circle);
    }

    /// <summary>
    /// 布局失败（工作区小于两圈）时「+」的兜底位置：钳进工作区；若离球太近则往内侧让开。
    /// 仅用于诊断性极端场景，不参与常规布局。
    /// </summary>
    private static Point GetFallbackPlusPosition(Point ballCenter, Rect workArea)
    {
        double half = CircleSize / 2;
        double left = workArea.Left + half;
        double right = workArea.Right - half;
        double top = workArea.Top + half;
        double bottom = workArea.Bottom - half;

        if (right < left || bottom < top)
        {
            return new Point(workArea.Left + workArea.Width / 2, workArea.Top + workArea.Height / 2);
        }

        var candidate = new Point(
            Math.Clamp(ballCenter.X, left, right),
            Math.Clamp(ballCenter.Y, top, bottom));

        if ((candidate - ballCenter).Length >= MinDistanceFromBall)
        {
            return candidate;
        }

        // 优先向下、向上、向右、向左让开一个最小距离，取第一个仍在工作区内的位置。
        foreach ((double dx, double dy) in new[] { (0.0, MinDistanceFromBall), (0.0, -MinDistanceFromBall),
                     (MinDistanceFromBall, 0.0), (-MinDistanceFromBall, 0.0) })
        {
            var shifted = new Point(candidate.X + dx, candidate.Y + dy);
            if (shifted.X >= left && shifted.X <= right && shifted.Y >= top && shifted.Y <= bottom)
            {
                return shifted;
            }
        }

        return candidate;
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

        circle.MouseLeftButtonUp += (_, args) =>
        {
            args.Handled = true;
            ShortcutActions.Execute(shortcut);
            Close();
        };

        circle.MouseRightButtonUp += (_, args) =>
        {
            args.Handled = true;
            ShowCircleMenu(circle, shortcut);
        };

        return circle;
    }

    private Border CreatePlusCircle()
    {
        Border circle = CreateCircleBase("添加快捷启动", CreateLabel("+", 18));

        circle.MouseLeftButtonUp += (_, args) =>
        {
            args.Handled = true;
            OnAddClick();
        };

        circle.MouseRightButtonUp += (_, args) =>
        {
            // 0 项时「+」也提供恢复默认（否则没有可右键的快捷圆）。
            if (_store.Shortcuts.Count == 0)
            {
                args.Handled = true;
                ShowRestoreDefaultsMenu(circle);
            }
        };

        return circle;
    }

    private static Border CreateCircleBase(string tooltip, UIElement content)
    {
        var circle = new Border
        {
            Width = CircleSize,
            Height = CircleSize,
            CornerRadius = new CornerRadius(CircleSize / 2),
            ToolTip = tooltip,
            Cursor = Cursors.Hand,
            Child = content
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

        var deleteItem = new MenuItem { Header = "删除" };
        deleteItem.Click += (_, _) =>
        {
            _store.Remove(shortcut);
            RebuildRing();
        };

        var changeItem = new MenuItem { Header = "更换程序" };
        changeItem.Click += (_, _) => ReplaceShortcut(shortcut);

        var resetItem = new MenuItem { Header = "恢复默认" };
        resetItem.Click += (_, _) =>
        {
            _store.ResetToDefaults();
            RebuildRing();
        };

        menu.Items.Add(deleteItem);
        menu.Items.Add(changeItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(resetItem);
        menu.PlacementTarget = circle;
        menu.IsOpen = true;
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

    private void OnAddClick()
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
        if (shortcut.Kind == ShortcutKind.App)
        {
            string name = Path.GetFileNameWithoutExtension(shortcut.Name);
            return string.IsNullOrWhiteSpace(name) ? "应用" : name[..Math.Min(2, name.Length)];
        }

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
}
