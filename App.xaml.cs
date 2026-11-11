using System.Diagnostics;
using System.Security.Principal;
using System.Threading;
using System.Windows;

namespace AssistantTouch;

/// <summary>
/// 应用入口：先做单实例互斥，再创建主窗口。
/// </summary>
/// <remarks>
/// 这里不再使用 XAML 的 <c>StartupUri</c>，而是手动创建 <see cref="MainWindow"/>：
/// 这样第二个实例在 <see cref="OnStartup"/> 里直接返回时，不会先创建/闪现一个主窗口，
/// 也就不会读写位置文件。
/// </remarks>
public partial class App : Application
{
    private Mutex? _singleInstanceMutex;
    private bool _ownsSingleInstanceMutex;
    private TrayIconController? _trayIconController;

    protected override void OnStartup(StartupEventArgs e)
    {
        bool createdNew;
        try
        {
            _singleInstanceMutex = CreateSingleInstanceMutex(out createdNew);
        }
        catch (UnauthorizedAccessException ex)
        {
            // Global\ 与 Local\ 都拿不到互斥量：按「已有实例」处理，直接退出，不建窗口/不写配置。
            Debug.WriteLine($"Single-instance mutex creation failed: {ex.Message}");
            Shutdown();
            return;
        }

        if (!createdNew)
        {
            // 已有实例在运行：立即退出。不创建主窗口、不写位置文件、不弹窗。
            _singleInstanceMutex.Dispose();
            _singleInstanceMutex = null;
            Shutdown();
            return;
        }

        _ownsSingleInstanceMutex = true;
        base.OnStartup(e);

        var mainWindow = new MainWindow();
        MainWindow = mainWindow;
        mainWindow.Show();
        _trayIconController = new TrayIconController(mainWindow);
    }

    /// <summary>
    /// 创建 per-user 单实例互斥量：优先 <c>Global\AssistantTouch.SingleInstance.&lt;SID&gt;</c>
    /// （同一用户跨会话互斥、不同用户各一个）；权限不足时回退 <c>Local\AssistantTouch.SingleInstance.&lt;SID&gt;</c>。
    /// </summary>
    /// <exception cref="UnauthorizedAccessException">Global\ 与 Local\ 都无法创建。</exception>
    private static Mutex CreateSingleInstanceMutex(out bool createdNew)
    {
        string userSid = WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName;
        string globalName = $@"Global\AssistantTouch.SingleInstance.{userSid}";

        try
        {
            return new Mutex(initiallyOwned: true, globalName, out createdNew);
        }
        catch (UnauthorizedAccessException)
        {
            Debug.WriteLine($"Global single-instance mutex unavailable; falling back to Local. ({globalName})");
            string localName = $@"Local\AssistantTouch.SingleInstance.{userSid}";
            return new Mutex(initiallyOwned: true, localName, out createdNew);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _trayIconController?.Dispose();
        _trayIconController = null;

        if (_ownsSingleInstanceMutex && _singleInstanceMutex is not null)
        {
            try
            {
                _singleInstanceMutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // 异常路径下可能已不再持有互斥量；退出流程不能因此中断。
            }
        }

        _singleInstanceMutex?.Dispose();
        _singleInstanceMutex = null;
        _ownsSingleInstanceMutex = false;

        base.OnExit(e);
    }
}
