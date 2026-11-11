using Microsoft.Win32;

namespace AssistantTouch;

/// <summary>
/// 开机自启：HKCU <c>Software\Microsoft\Windows\CurrentVersion\Run</c>，值名 <c>AssistantTouch</c>。
/// </summary>
/// <remarks>
/// 只写当前用户（HKCU），不碰 HKLM、不要求管理员权限。
/// 读取/写入失败（组策略、权限受限）时按「未启用」处理，不抛异常、不影响菜单操作。
/// </remarks>
internal static class AutostartService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "AssistantTouch";

    /// <summary>注册表里的值与当前 exe 路径一致才算启用。</summary>
    public static bool IsEnabled()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            string? value = key?.GetValue(ValueName) as string;
            return string.Equals(value, BuildCommand(), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>启用/禁用开机自启（带引号的 exe 完整路径）。</summary>
    public static void SetEnabled(bool enabled)
    {
        try
        {
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
            if (enabled)
            {
                key.SetValue(ValueName, BuildCommand());
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }
        }
        catch (Exception)
        {
            // 写失败时保持现状，界面以 IsEnabled() 的实际读回为准。
        }
    }

    private static string BuildCommand()
    {
        string? exePath = Environment.ProcessPath;
        return string.IsNullOrWhiteSpace(exePath) ? string.Empty : $"\"{exePath}\"";
    }
}
