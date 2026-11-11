using System.IO;
using System.Text.Json;

namespace AssistiveTouch;

/// <summary>
/// 悬浮球位置持久化：<c>%LOCALAPPDATA%\AssistiveTouch\window.json</c>，
/// 内容形如 <c>{"left": 924, "top": 480}</c>，单位是 WPF DIU（不是物理像素、不是窗口句柄）。
/// </summary>
/// <remarks>
/// 读：文件不存在 / JSON 损坏 / 字段缺失 / 字段不是数字，一律按「没有保存过位置」处理，
/// 由调用方回落 XAML 的 CenterScreen；任何异常都不得抛到 UI 线程。
/// 写：先写同目录临时文件（文件名含进程 ID，多实例互不干扰）再覆盖移动，避免留下半截 JSON；写盘失败静默忽略，
/// 不能因为磁盘/权限问题阻断拖拽或退出。
/// </remarks>
internal static class WindowPositionStore
{
    private static readonly string DirectoryPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AssistiveTouch");

    /// <summary>位置文件的完整路径。</summary>
    public static string FilePath { get; } = Path.Combine(DirectoryPath, "window.json");

    /// <summary>
    /// 读取上次保存的位置。成功返回 <c>true</c>；缺失或非法返回 <c>false</c>（不抛异常）。
    /// </summary>
    public static bool TryLoad(out double left, out double top)
    {
        left = 0;
        top = 0;

        try
        {
            if (!File.Exists(FilePath))
            {
                return false;
            }

            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(FilePath));
            JsonElement root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("left", out JsonElement leftElement) ||
                !root.TryGetProperty("top", out JsonElement topElement) ||
                leftElement.ValueKind != JsonValueKind.Number ||
                topElement.ValueKind != JsonValueKind.Number ||
                !leftElement.TryGetDouble(out double parsedLeft) ||
                !topElement.TryGetDouble(out double parsedTop) ||
                !double.IsFinite(parsedLeft) ||
                !double.IsFinite(parsedTop))
            {
                return false;
            }

            left = parsedLeft;
            top = parsedTop;
            return true;
        }
        catch (Exception)
        {
            // 损坏的 JSON、无读权限、并发写坏等，都按「没有保存过位置」处理。
            return false;
        }
    }

    /// <summary>
    /// 保存位置。任何写盘失败都静默吞掉，不影响拖拽与退出。
    /// </summary>
    public static void Save(double left, double top)
    {
        if (!double.IsFinite(left) || !double.IsFinite(top))
        {
            return;
        }

        // 临时文件名带进程 ID：多实例同时保存时不会互相覆盖/删除对方的临时文件。
        string tempPath = $"{FilePath}.{Environment.ProcessId}.tmp";

        try
        {
            Directory.CreateDirectory(DirectoryPath);
            File.WriteAllText(tempPath, JsonSerializer.Serialize(new { left, top }));
            File.Move(tempPath, FilePath, overwrite: true);
        }
        catch (Exception)
        {
            // 存储不可用（只读盘、磁盘满、被占用等）不应阻断交互与退出。
            try
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
            catch (Exception)
            {
                // 临时文件清理失败同样不再处理。
            }
        }
    }
}
