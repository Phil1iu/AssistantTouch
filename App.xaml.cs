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
    // 选 Local\ 而不是 Global\：本应用是每用户桌面工具，按登录会话隔离即可；
    // Global\ 在多用户会话、受限权限或提权场景下更容易撞权限问题。
    private const string SingleInstanceMutexName = @"Local\AssistantTouch.SingleInstance";

    private Mutex? _singleInstanceMutex;
    private bool _ownsSingleInstanceMutex;
    private TrayIconController? _trayIconController;

    protected override void OnStartup(StartupEventArgs e)
    {
        _singleInstanceMutex = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out bool createdNew);

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
