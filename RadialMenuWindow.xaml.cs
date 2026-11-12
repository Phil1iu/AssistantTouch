using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace AssistantTouch;

/// <summary>
/// 左键单击大圆后出现的径向快捷菜单：全屏透明置顶窗口，周围均匀分布快捷小圆。
/// 点非圆区域或 Esc 关闭；点击小圆执行动作并关闭。
/// </summary>
public partial class RadialMenuWindow : Window
{
    private const double CircleSize = 44;
    private const double RingRadius = 95;
    private const double RingExtent = RingRadius + CircleSize / 2;

    private readonly IReadOnlyList<ShortcutDefinition> _shortcuts;
    private readonly Point _ballCenterScreen;

    internal RadialMenuWindow(IReadOnlyList<ShortcutDefinition> shortcuts, Point ballCenterScreen)
    {
        InitializeComponent();

        _shortcuts = shortcuts;
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

        if (_shortcuts.Count == 0)
        {
            return;
        }

        Point ballCenter = PointFromScreen(_ballCenterScreen);
        Rect workArea = GetWorkAreaClientRect(_ballCenterScreen);

        // 整体钳进当前显示器工作区；大圆保持原位，只在必要时偏移圆心。
        double centerX = Math.Max(workArea.Left + RingExtent, Math.Min(workArea.Right - RingExtent, ballCenter.X));
        double centerY = Math.Max(workArea.Top + RingExtent, Math.Min(workArea.Bottom - RingExtent, ballCenter.Y));

        for (int i = 0; i < _shortcuts.Count; i++)
        {
            double angle = -Math.PI / 2 + 2 * Math.PI * i / _shortcuts.Count;
            double x = centerX + RingRadius * Math.Cos(angle);
            double y = centerY + RingRadius * Math.Sin(angle);

            Border circle = CreateCircle(_shortcuts[i]);
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

    private Border CreateCircle(ShortcutDefinition shortcut)
    {
        var circle = new Border
        {
            Width = CircleSize,
            Height = CircleSize,
            CornerRadius = new CornerRadius(CircleSize / 2),
            ToolTip = shortcut.Name,
            Cursor = Cursors.Hand,
            Child = CreateCircleContent(shortcut)
        };

        var style = new Style(typeof(Border));
        style.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(Color.FromArgb(0xF5, 255, 255, 255))));
        style.Setters.Add(new Setter(Border.BorderBrushProperty, new SolidColorBrush(Color.FromArgb(0x80, 0, 0, 0))));
        style.Setters.Add(new Setter(Border.BorderThicknessProperty, new Thickness(1)));
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Border.BorderBrushProperty, new SolidColorBrush(Color.FromArgb(0xA6, 0, 0, 0))));
        style.Triggers.Add(hover);
        circle.Style = style;

        circle.MouseLeftButtonUp += (_, args) =>
        {
            args.Handled = true;
            ShortcutActions.Execute(shortcut);
            Close();
        };

        return circle;
    }

    private static UIElement CreateCircleContent(ShortcutDefinition shortcut)
    {
        return new TextBlock
        {
            Text = GetShortLabel(shortcut),
            FontSize = 13,
            Foreground = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false
        };
    }

    private static string GetShortLabel(ShortcutDefinition shortcut)
    {
        if (shortcut.Kind == ShortcutKind.App)
        {
            return string.IsNullOrWhiteSpace(shortcut.Name) ? "应用" : shortcut.Name[..Math.Min(2, shortcut.Name.Length)];
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
