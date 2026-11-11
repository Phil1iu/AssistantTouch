using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace AssistantTouch;

/// <summary>
/// 托盘图标与托盘菜单：显示/隐藏球、重置位置、关于、退出；双击托盘图标切换显隐。
/// </summary>
/// <remarks>
/// 图标运行时绘制（白色圆 + 1px 灰环，与悬浮球一致），用 <see cref="Bitmap.GetHicon"/> 转成 HICON；
/// <see cref="Icon.FromHandle"/> 不拥有句柄，Dispose 时必须自己 <c>DestroyIcon</c>。
/// 托盘菜单是 WinForms <see cref="ContextMenuStrip"/>，不参与 WPF 的浅色主题。
/// </remarks>
internal sealed class TrayIconController : IDisposable
{
    private readonly MainWindow _window;
    private readonly NotifyIcon _notifyIcon;
    private readonly ContextMenuStrip _menu;
    private readonly ToolStripMenuItem _toggleItem;
    private readonly Icon _icon;
    private readonly IntPtr _iconHandle;
    private bool _disposed;

    public TrayIconController(MainWindow window)
    {
        _window = window;
        _iconHandle = CreateIconHandle();
        _icon = Icon.FromHandle(_iconHandle);

        _toggleItem = new ToolStripMenuItem("隐藏悬浮球");
        _toggleItem.Click += (_, _) => ToggleBall();

        var resetItem = new ToolStripMenuItem("重置位置");
        resetItem.Click += (_, _) => _window.ResetToPrimaryCenter();

        var aboutItem = new ToolStripMenuItem("关于");
        aboutItem.Click += (_, _) => _window.ShowAboutDialog();

        var exitItem = new ToolStripMenuItem("退出");
        exitItem.Click += (_, _) => _window.RequestExit();

        _menu = new ContextMenuStrip();
        _menu.Items.Add(_toggleItem);
        _menu.Items.Add(resetItem);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(aboutItem);
        _menu.Items.Add(exitItem);
        _menu.Opening += (_, _) => UpdateToggleText();

        _notifyIcon = new NotifyIcon
        {
            Icon = _icon,
            Text = "AssistantTouch",
            Visible = true,
            ContextMenuStrip = _menu
        };
        _notifyIcon.DoubleClick += (_, _) => ToggleBall();
    }

    private void ToggleBall()
    {
        if (_window.IsVisible)
        {
            _window.Hide();
        }
        else
        {
            _window.Show();
        }

        UpdateToggleText();
    }

    private void UpdateToggleText()
    {
        _toggleItem.Text = _window.IsVisible ? "隐藏悬浮球" : "显示悬浮球";
    }

    private static IntPtr CreateIconHandle()
    {
        using var bitmap = new Bitmap(32, 32);
        using (Graphics graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.Clear(Color.Transparent);

            var circle = new Rectangle(3, 3, 26, 26);
            using var fill = new SolidBrush(Color.FromArgb(245, 255, 255, 255));
            using var ring = new Pen(Color.FromArgb(200, 120, 120, 120), 1f);
            graphics.FillEllipse(fill, circle);
            graphics.DrawEllipse(ring, circle);
        }

        return bitmap.GetHicon();
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr handle);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _menu.Dispose();
        _icon.Dispose();

        if (_iconHandle != IntPtr.Zero)
        {
            DestroyIcon(_iconHandle);
        }
    }
}
