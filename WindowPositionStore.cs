using System.IO;
using System.Text.Json;

namespace AssistantTouch;

/// <summary>
/// 悬浮球位置持久化：<c>%LOCALAPPDATA%\AssistantTouch\window.json</c>，
/// 内容形如 <c>{"left": 924, "top": 480}</c>，贴边收起时另带 <c>"edge": "left"</c>，
/// 单位是 WPF DIU（不是物理像素、不是窗口句柄）。
/// </summary>
/// <remarks>
/// 读：文件不存在 / JSON 损坏 / 字段缺失 / 字段不是数字，一律按「没有保存过位置」处理，
/// 由调用方回落 XAML 的 CenterScreen；任何异常都不得抛到 UI 线程。
/// 迁移：sprint-05 从 <c>AssistiveTouch</c> 改名而来。新路径不存在、旧路径
/// <c>%LOCALAPPDATA%\AssistiveTouch\window.json</c> 存在且合法时，读一次并立即写入新路径
/// （不删除旧文件）；之后只读写新路径。
/// 贴边（sprint-24）：<c>edge</c> 缺省 / 不是 left|right|top|bottom 时按「未贴边」处理，
/// 旧版本文件因此可原样读取；<c>left/top</c> 始终保存「展开态」坐标（收起态坐标在工作区外，
/// 重启后按边重算，分辨率变化也不会把球留在屏幕外）。
/// 写：先写同目录临时文件（文件名含进程 ID，多实例互不干扰）再覆盖移动，避免留下半截 JSON；写盘失败静默忽略，
/// 不能因为磁盘/权限问题阻断拖拽或退出。
/// 落盘不变量（sprint-24 rev-24 F1）：只接受 <see cref="SavedPosition"/>，贴边态必须经
/// <see cref="ComposeSavedPosition"/> 得到「展开位置 + 边」，调用方拿不到「贴边却存收起坐标」的写法。
/// </remarks>
internal static class WindowPositionStore
{
    private static readonly string LocalAppDataPath =
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    private static readonly string DirectoryPath = Path.Combine(LocalAppDataPath, "AssistantTouch");

    /// <summary>位置文件的完整路径。</summary>
    public static string FilePath { get; } = Path.Combine(DirectoryPath, "window.json");

    /// <summary>
    /// 改名前的旧位置文件路径：只用于一次性迁移（sprint-05：AssistiveTouch → AssistantTouch）。
    /// </summary>
    private static readonly string LegacyFilePath =
        Path.Combine(LocalAppDataPath, "AssistiveTouch", "window.json");

    /// <summary>
    /// 读取上次保存的位置与贴边状态。成功返回 <c>true</c>；缺失或非法返回 <c>false</c>（不抛异常）。
    /// 旧文件没有 <c>edge</c> 字段时为 <see cref="DockEdge.None"/>。
    /// </summary>
    public static bool TryLoad(out double left, out double top, out DockEdge edge)
    {
        left = 0;
        top = 0;
        edge = DockEdge.None;

        if (TryLoadFrom(FilePath, out left, out top, out edge))
        {
            return true;
        }

        // 只有「新文件不存在」才回退旧路径；新文件存在但损坏时按「没有可用位置」处理，
        // 避免用旧文件覆盖用户改名后已经产生的新文件。
        if (File.Exists(FilePath))
        {
            return false;
        }

        if (!TryLoadFrom(LegacyFilePath, out left, out top, out edge))
        {
            return false;
        }

        // 一次性迁移：旧位置写入新路径（不删旧文件）；写失败不影响本次恢复结果。
        Save(new SavedPosition(left, top, edge));
        return true;
    }

    private static bool TryLoadFrom(string path, out double left, out double top, out DockEdge edge)
    {
        left = 0;
        top = 0;
        edge = DockEdge.None;

        try
        {
            if (!File.Exists(path))
            {
                return false;
            }

            return TryParse(File.ReadAllText(path), out left, out top, out edge);
        }
        catch (Exception)
        {
            // 无读权限、并发写坏等，都按「没有保存过位置」处理。
            left = 0;
            top = 0;
            edge = DockEdge.None;
            return false;
        }
    }

    /// <summary>
    /// 解析位置 JSON（纯函数，便于单测），不关心贴边状态。合法返回 <c>true</c>。
    /// </summary>
    internal static bool TryParse(string? json, out double left, out double top)
    {
        return TryParse(json, out left, out top, out _);
    }

    /// <summary>
    /// 解析位置 JSON（纯函数，便于单测）：合法返回 <c>true</c>；
    /// 空串 / 损坏 / 缺字段 / 非数字 / NaN / Infinity / 非对象根 均返回 <c>false</c>，不抛异常。
    /// <c>edge</c> 缺失或不是 left|right|top|bottom 时为 <see cref="DockEdge.None"/>（不影响位置是否合法）。
    /// </summary>
    internal static bool TryParse(string? json, out double left, out double top, out DockEdge edge)
    {
        left = 0;
        top = 0;
        edge = DockEdge.None;

        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
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
            edge = ParseEdge(root);
            return true;
        }
        catch (Exception)
        {
            // 损坏的 JSON 一律按「没有保存过位置」处理。
            return false;
        }
    }

    /// <summary>读 <c>edge</c> 字段：只接受 left/right/top/bottom（忽略大小写），其它一律按未贴边。</summary>
    private static DockEdge ParseEdge(JsonElement root)
    {
        if (!root.TryGetProperty("edge", out JsonElement edgeElement) ||
            edgeElement.ValueKind != JsonValueKind.String)
        {
            return DockEdge.None;
        }

        string? raw = edgeElement.GetString();
        if (string.IsNullOrWhiteSpace(raw) ||
            !Enum.TryParse(raw, ignoreCase: true, out DockEdge edge) ||
            !Enum.IsDefined(typeof(DockEdge), edge))
        {
            return DockEdge.None;
        }

        return edge;
    }

    /// <summary>
    /// 一次落盘的内容：<see cref="Left"/>/<see cref="Top"/> 是<strong>展开态</strong>坐标，<see cref="Edge"/> 是贴边状态。
    /// </summary>
    /// <remarks>
    /// 只开放给 <see cref="ComposeSavedPosition"/> 构造，避免调用方直接拼出「贴边却存收起坐标」的条目：
    /// 收起坐标在工作区外，存进去以后重启会被钳回工作区并丢掉贴边。
    /// </remarks>
    internal readonly record struct SavedPosition(double Left, double Top, DockEdge Edge);

    /// <summary>
    /// 由窗口当前状态算出该落盘的内容（纯函数，便于单测）：
    /// 未贴边存当前位置；贴边一律存「展开位置 + 边」，与窗口此刻（可能是屏外的）收起坐标无关。
    /// </summary>
    /// <param name="edge">当前贴边状态。</param>
    /// <param name="expandedLeft">贴边时球展开后应在的左上角 X（贴边态才有效）。</param>
    /// <param name="expandedTop">贴边时球展开后应在的左上角 Y（贴边态才有效）。</param>
    /// <param name="currentLeft">窗口此刻的左上角 X（未贴边时就是展开位置）。</param>
    /// <param name="currentTop">窗口此刻的左上角 Y（未贴边时就是展开位置）。</param>
    internal static SavedPosition ComposeSavedPosition(
        DockEdge edge, double expandedLeft, double expandedTop, double currentLeft, double currentTop)
    {
        return edge == DockEdge.None
            ? new SavedPosition(currentLeft, currentTop, DockEdge.None)
            : new SavedPosition(expandedLeft, expandedTop, edge);
    }

    /// <summary>把落盘内容序列化成文件文本（纯函数，便于单测）。未贴边时保持旧格式，不写 <c>edge</c>。</summary>
    internal static string Serialize(SavedPosition position)
    {
        return position.Edge == DockEdge.None
            ? JsonSerializer.Serialize(new { left = position.Left, top = position.Top })
            : JsonSerializer.Serialize(new
            {
                left = position.Left,
                top = position.Top,
                edge = position.Edge.ToString().ToLowerInvariant()
            });
    }

    /// <summary>
    /// 保存位置与贴边状态。任何写盘失败都静默吞掉，不影响拖拽与退出。
    /// </summary>
    /// <remarks>
    /// 入参只能用 <see cref="SavedPosition"/>：贴边态必须由 <see cref="ComposeSavedPosition"/> 生成，
    /// 从类型上堵住「贴边却把屏外收起坐标当普通位置存」的误用（rev-24 F1）。
    /// </remarks>
    public static void Save(SavedPosition position)
    {
        if (!double.IsFinite(position.Left) || !double.IsFinite(position.Top))
        {
            return;
        }

        // 临时文件名带进程 ID：多实例同时保存时不会互相覆盖/删除对方的临时文件。
        string tempPath = $"{FilePath}.{Environment.ProcessId}.tmp";

        try
        {
            Directory.CreateDirectory(DirectoryPath);
            File.WriteAllText(tempPath, Serialize(position));
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
