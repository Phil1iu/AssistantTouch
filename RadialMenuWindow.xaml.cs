using System.IO;
using System.Windows;
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
    private const double RingExtent = RingRadius + CircleSize / 2;

    private readonly ShortcutStore _store;
    private readonly Point _ballCenterScreen;

    internal RadialMenuWindow(ShortcutStore store, Point ballCenterScreen)
    {
        InitializeComponent();

        _store = store;
        _ballCenterScreen = ballCenterScreen;

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

    private void RebuildRing()
    {
        RingCanvas.Children.Clear();

        int shortcutCount = _store.Shortcuts.Count;
        int total = shortcutCount + 1; // + 「+」圆

        Point ballCenter = PointFromScreen(_ballCenterScreen);
        Rect workArea = GetWorkAreaClientRect(_ballCenterScreen);

        // 整体钳进当前显示器工作区；大圆保持原位，只在必要时偏移圆心。
        double centerX = Math.Max(workArea.Left + RingExtent, Math.Min(workArea.Right - RingExtent, ballCenter.X));
        double centerY = Math.Max(workArea.Top + RingExtent, Math.Min(workArea.Bottom - RingExtent, ballCenter.Y));

        for (int i = 0; i < total; i++)
        {
            double angle = -Math.PI / 2 + 2 * Math.PI * i / total;
            double x = centerX + RingRadius * Math.Cos(angle);
            double y = centerY + RingRadius * Math.Sin(angle);

            Border circle = i < shortcutCount ? CreateShortcutCircle(_store.Shortcuts[i]) : CreatePlusCircle();
            Canvas.SetLeft(circle, x - CircleSize / 2);
            Canvas.SetTop(circle, y - CircleSize / 2);
            RingCanvas.Children.Add(circle);
        }
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
        RebuildRing();
    }

    private void OnAddClick()
    {
        if (_store.Shortcuts.Count >= ShortcutStore.MaxShortcuts)
        {
            MessageBox.Show(this, "最多 8 个快捷启动", "AssistantTouch", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

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
            MessageBox.Show(this, "最多 8 个快捷启动", "AssistantTouch", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

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

        return dialog.ShowDialog(this) == true ? dialog.FileName : null;
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
