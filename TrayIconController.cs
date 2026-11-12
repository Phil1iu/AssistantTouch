using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace AssistantTouch;

/// <summary>
/// 托盘图标与托盘菜单：显示/隐藏球、重置位置、关于、退出；双击托盘图标切换显隐。
/// </summary>
/// <remarks>
/// 图标优先从程序集资源 <c>assets/app.ico</c> 加载（与 exe 的 ApplicationIcon 同一个文件，多尺寸）；
/// 资源不可用时回退到运行时绘制（白色圆 + 1px 灰环）。回退图标用 <see cref="Bitmap.GetHicon"/>
/// 生成 HICON，<see cref="Icon.FromHandle"/> 不拥有句柄，Dispose 时必须自己 <c>DestroyIcon</c>。
/// 托盘菜单是 WinForms <see cref="ContextMenuStrip"/>，不参与 WPF 的浅色主题。
/// </remarks>
internal sealed class TrayIconController : IDisposable
{
    private const string IconResourceUri = "pack://application:,,,/assets/app.ico";

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
        (_icon, _iconHandle) = LoadIcon();

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
            // 隐藏球前先关闭径向菜单，避免留下一个全屏透明窗口。
            _window.CloseRadialMenu();
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

    /// <summary>
    /// 优先加载程序集内嵌的多尺寸图标；加载失败时回退到运行时绘制（返回需 DestroyIcon 的句柄）。
    /// </summary>
    private static (Icon Icon, IntPtr Handle) LoadIcon()
    {
        try
        {
            var resource = System.Windows.Application.GetResourceStream(new Uri(IconResourceUri));
            if (resource is not null)
            {
                using Stream stream = resource.Stream;
                return (new Icon(stream), IntPtr.Zero);
            }
        }
        catch (Exception)
        {
            // 资源缺失或损坏：回退到运行时绘制。
        }

        IntPtr handle = CreateIconHandle();
        return (Icon.FromHandle(handle), handle);
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

        try
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
        }
        finally
        {
            // 即使 NotifyIcon 释放失败，也确保菜单、图标与 HICON 被释放。
            try
            {
                _menu.Dispose();
            }
            catch (Exception)
            {
            }

            try
            {
                _icon.Dispose();
            }
            catch (Exception)
            {
            }

            if (_iconHandle != IntPtr.Zero)
            {
                try
                {
                    DestroyIcon(_iconHandle);
                }
                catch (Exception)
                {
                }
            }
        }
    }
}
