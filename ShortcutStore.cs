using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;

namespace AssistantTouch;

/// <summary>
/// 快捷方式列表的加载/保存：<c>%LOCALAPPDATA%\AssistantTouch\shortcuts.json</c>。
/// 文件缺失、损坏、字段非法或超过 8 个时回落到默认 6 个系统快捷方式，不崩溃。
/// 每次加载都会覆盖写 <c>shortcuts-load.log</c>，记录路径/存在性/长度/解析结果/条数/首个失败原因。
/// </summary>
internal sealed class ShortcutStore
{
    public const int MaxShortcuts = 8;

    /// <summary>回落态首次保存前备份原文件失败（拒绝保存）。</summary>
    public const string FallbackBackupFailed = "fallback-backup-failed";

    /// <summary>写临时文件/覆盖移动失败。</summary>
    public const string WriteFailed = "write-failed";

    /// <summary>生产保存时保留的滚动备份数量。</summary>
    internal const int RollingBackupsToKeep = 3;

    internal const string RollingBackupPrefix = "shortcuts.bak-";

    private static readonly HashSet<string> KnownSystemActions =
        new(SystemShortcutCatalog.All.Select(entry => entry.Action), StringComparer.Ordinal);

    private static readonly string DirectoryPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AssistantTouch");

    private readonly List<ShortcutDefinition> _shortcuts;
    private readonly string _filePath;
    private readonly string _directoryPath;
    private readonly string _logFilePath;
    private readonly IShortcutFileSystem _fileSystem;
    private readonly bool _isFallback;
    private readonly string? _fallbackReason;
    private bool _fallbackBackedUp;

    /// <summary>滚动备份名内序号：同一毫秒内多次保存时仍能按文件名排序。</summary>
    private int _rollingBackupSequence;

    /// <summary>仅测试用：捕获 Save 的内容，避免写真实 %LOCALAPPDATA%。</summary>
    private readonly Action<IReadOnlyList<ShortcutDefinition>>? _saveHook;

    private ShortcutStore(
        List<ShortcutDefinition> shortcuts,
        string filePath,
        string logFilePath,
        IShortcutFileSystem fileSystem,
        bool isFallback = false,
        string? fallbackReason = null,
        Action<IReadOnlyList<ShortcutDefinition>>? saveHook = null)
    {
        _shortcuts = shortcuts;
        _filePath = filePath;
        _directoryPath = Path.GetDirectoryName(filePath) ?? ".";
        _logFilePath = logFilePath;
        _fileSystem = fileSystem;
        _isFallback = isFallback;
        _fallbackReason = fallbackReason;
        _saveHook = saveHook;
    }

    /// <summary>仅测试用：构造内存存储；唯一临时路径 + 内存文件系统 + 显式保存钩子，绝不触碰真实 %LOCALAPPDATA%。</summary>
    internal static ShortcutStore CreateForTests(
        IEnumerable<ShortcutDefinition> shortcuts,
        Action<IReadOnlyList<ShortcutDefinition>>? saveHook = null)
    {
        // 路径仅用于诊断；InMemoryShortcutFileSystem 不落盘，即使保存钩子被绕过也不会写真实配置。
        string testDirectory = Path.Combine(
            Path.GetTempPath(),
            "AssistantTouchTests",
            Guid.NewGuid().ToString("N"));

        return new ShortcutStore(
            shortcuts.ToList(),
            Path.Combine(testDirectory, "shortcuts.json"),
            Path.Combine(testDirectory, "shortcuts-load.log"),
            new InMemoryShortcutFileSystem(),
            saveHook: saveHook ?? (_ => { }));
    }

    /// <summary>本次加载是否因「文件存在但读/解析失败」回落到默认列表。</summary>
    public bool IsFallback => _isFallback;

    /// <summary>回落原因（解析错误或 IO 异常）；未回落为 null。</summary>
    public string? FallbackReason => _fallbackReason;

    /// <summary>回落态的原文件是否已成功备份。</summary>
    public bool FallbackBackupCreated => _fallbackBackedUp;

    /// <summary>最近一次编辑/保存失败的原因（<see cref="FallbackBackupFailed"/> / <see cref="WriteFailed"/>）。</summary>
    public string? LastSaveFailure { get; private set; }

    /// <summary>快捷方式文件的完整路径。</summary>
    public static string FilePath { get; } = Path.Combine(DirectoryPath, "shortcuts.json");

    /// <summary>最近一次加载的诊断日志路径（覆盖写）。</summary>
    public static string LogFilePath { get; } = Path.Combine(DirectoryPath, "shortcuts-load.log");

    public IReadOnlyList<ShortcutDefinition> Shortcuts => _shortcuts;

    public static ShortcutStore Load()
    {
        return Load(FilePath, LogFilePath, new ShortcutFileSystem());
    }

    /// <summary>可注入 IO 的加载（测试用）：区分「文件不存在」与「存在但读/解析失败」。</summary>
    internal static ShortcutStore Load(string filePath, string logFilePath, IShortcutFileSystem fileSystem)
    {
        bool exists = false;
        string? text = null;

        try
        {
            exists = fileSystem.FileExists(filePath);

            if (exists)
            {
                text = fileSystem.ReadAllText(filePath);

                if (TryParse(text, out List<ShortcutDefinition>? parsed, out string? error) && parsed is not null)
                {
                    WriteLog(fileSystem, logFilePath, filePath, exists: true, length: text.Length,
                        parsed: true, count: parsed.Count, error: null, fallback: false, text: text);
                    return new ShortcutStore(parsed, filePath, logFilePath, fileSystem);
                }

                string reason = error ?? "unknown";
                WriteLog(fileSystem, logFilePath, filePath, exists: true, length: text.Length,
                    parsed: false, count: 0, error: reason, fallback: true, text: text);
                return new ShortcutStore(
                    ShortcutDefinition.CreateDefaults(),
                    filePath,
                    logFilePath,
                    fileSystem,
                    isFallback: true,
                    fallbackReason: reason);
            }

            WriteLog(fileSystem, logFilePath, filePath, exists: false, length: 0,
                parsed: false, count: 0, error: "missing", fallback: false, text: null);
        }
        catch (Exception exception)
        {
            string reason = exception.GetType().Name + ": " + exception.Message;
            WriteLog(fileSystem, logFilePath, filePath, exists: exists, length: text?.Length ?? -1,
                parsed: false, count: 0, error: reason, fallback: true, text: text);
            return new ShortcutStore(
                ShortcutDefinition.CreateDefaults(),
                filePath,
                logFilePath,
                fileSystem,
                isFallback: true,
                fallbackReason: reason);
        }

        // 文件不存在：首次运行，正常默认、允许保存、不触发备份。
        return new ShortcutStore(ShortcutDefinition.CreateDefaults(), filePath, logFilePath, fileSystem);
    }

    /// <summary>把本次加载结果覆盖写到 <c>shortcuts-load.log</c>；日志失败不影响加载。</summary>
    private static void WriteLog(
        IShortcutFileSystem fileSystem,
        string logFilePath,
        string filePath,
        bool exists,
        int length,
        bool parsed,
        int count,
        string? error,
        bool fallback,
        string? text)
    {
        try
        {
            fileSystem.CreateDirectory(Path.GetDirectoryName(logFilePath) ?? ".");

            string line = string.Join(
                " ",
                DateTime.Now.ToString("O"),
                "path=" + filePath,
                "exists=" + exists,
                "length=" + length,
                "parsed=" + parsed,
                "count=" + count,
                "error=" + (error ?? "-"),
                "fallback=" + (fallback ? "true" : "false"),
                "preview=" + BuildPreview(text));

            fileSystem.WriteAllText(logFilePath, line + Environment.NewLine);
        }
        catch (Exception)
        {
            // 诊断日志失败不影响加载。
        }
    }

    /// <summary>把原文压成单行预览（控制字符与 BOM 换成空格），最多 240 个字符。</summary>
    private static string BuildPreview(string? text)
    {
        if (text is null)
        {
            return "-";
        }

        const int maxLength = 240;
        var builder = new StringBuilder(Math.Min(text.Length, maxLength + 1));
        foreach (char character in text)
        {
            if (builder.Length >= maxLength)
            {
                builder.Append('…');
                break;
            }

            builder.Append(char.IsControl(character) || character == '\uFEFF' ? ' ' : character);
        }

        return builder.ToString();
    }

    public bool TryAdd(ShortcutDefinition shortcut)
    {
        LastSaveFailure = null;

        if (_shortcuts.Count >= MaxShortcuts)
        {
            return false;
        }

        if (!EnsureFallbackBackup())
        {
            return false;
        }

        _shortcuts.Add(shortcut);
        return Save();
    }

    public bool Remove(ShortcutDefinition shortcut)
    {
        LastSaveFailure = null;

        int index = _shortcuts.IndexOf(shortcut);
        if (index < 0)
        {
            return false;
        }

        if (!EnsureFallbackBackup())
        {
            return false;
        }

        _shortcuts.RemoveAt(index);
        return Save();
    }

    public bool Replace(ShortcutDefinition oldShortcut, ShortcutDefinition newShortcut)
    {
        LastSaveFailure = null;

        int index = _shortcuts.IndexOf(oldShortcut);
        if (index < 0)
        {
            return false;
        }

        if (!EnsureFallbackBackup())
        {
            return false;
        }

        _shortcuts[index] = newShortcut;
        return Save();
    }

    public int IndexOf(ShortcutDefinition shortcut) => _shortcuts.IndexOf(shortcut);

    /// <summary>
    /// 按 offset 上移（-1）/下移（+1）。越界、offset 为 0 或找不到时返回 false；成功立即保存。
    /// </summary>
    public bool Move(ShortcutDefinition shortcut, int offset)
    {
        LastSaveFailure = null;

        int index = _shortcuts.IndexOf(shortcut);
        if (index < 0 || offset == 0)
        {
            return false;
        }

        int target = index + offset;
        if (target < 0 || target >= _shortcuts.Count)
        {
            return false;
        }

        if (!EnsureFallbackBackup())
        {
            return false;
        }

        _shortcuts.RemoveAt(index);
        _shortcuts.Insert(target, shortcut);
        return Save();
    }

    /// <summary>交换两个快捷项的位置；找不到、同一个对象时返回 false；成功立即保存。</summary>
    public bool Swap(ShortcutDefinition first, ShortcutDefinition second)
    {
        LastSaveFailure = null;

        int firstIndex = _shortcuts.IndexOf(first);
        int secondIndex = _shortcuts.IndexOf(second);
        if (firstIndex < 0 || secondIndex < 0 || firstIndex == secondIndex)
        {
            return false;
        }

        if (!EnsureFallbackBackup())
        {
            return false;
        }

        (_shortcuts[firstIndex], _shortcuts[secondIndex]) = (_shortcuts[secondIndex], _shortcuts[firstIndex]);
        return Save();
    }

    /// <summary>
    /// 重命名：去首尾空格、非空、长度 ≤20。成功（且名称确实变化）时立即保存并返回 true。
    /// </summary>
    public bool Rename(ShortcutDefinition shortcut, string? newName)
    {
        LastSaveFailure = null;

        int index = _shortcuts.IndexOf(shortcut);
        if (index < 0 || !ShortcutName.TryNormalize(newName, out string normalized, out _))
        {
            return false;
        }

        ShortcutDefinition current = _shortcuts[index];
        if (string.Equals(current.Name, normalized, StringComparison.Ordinal))
        {
            return false;
        }

        if (!EnsureFallbackBackup())
        {
            return false;
        }

        // Name 是 init-only：保留 Kind/Action/Path，创建新实例替换。
        _shortcuts[index] = new ShortcutDefinition
        {
            Kind = current.Kind,
            Action = current.Action,
            Path = current.Path,
            Name = normalized
        };

        return Save();
    }

    public bool ResetToDefaults()
    {
        LastSaveFailure = null;

        if (!EnsureFallbackBackup())
        {
            return false;
        }

        _shortcuts.Clear();
        _shortcuts.AddRange(ShortcutDefinition.CreateDefaults());
        return Save();
    }

    /// <summary>解析 JSON（纯函数，internal 便于单测）：严格校验，任一非法项即返回 false。</summary>
    internal static bool TryParse(string? json, out List<ShortcutDefinition>? shortcuts)
    {
        return TryParse(json, out shortcuts, out _);
    }

    /// <summary>
    /// 解析 JSON，并在失败时通过 <paramref name="error"/> 返回首个失败原因（供诊断日志使用）。
    /// 严格校验：任一非法项即整体失败，调用方回落到默认列表。
    /// </summary>
    internal static bool TryParse(string? json, out List<ShortcutDefinition>? shortcuts, out string? error)
    {
        shortcuts = null;
        error = null;

        if (string.IsNullOrWhiteSpace(json))
        {
            error = "empty";
            return false;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                error = "root-not-object";
                return false;
            }

            if (!root.TryGetProperty("shortcuts", out JsonElement array))
            {
                error = "missing-shortcuts";
                return false;
            }

            if (array.ValueKind != JsonValueKind.Array)
            {
                error = "shortcuts-not-array";
                return false;
            }

            var parsed = new List<ShortcutDefinition>();
            int index = 0;

            foreach (JsonElement element in array.EnumerateArray())
            {
                string item = $"item[{index}]";

                if (element.ValueKind != JsonValueKind.Object)
                {
                    error = item + ": not-object";
                    return false;
                }

                if (!element.TryGetProperty("kind", out JsonElement kindElement) ||
                    kindElement.ValueKind != JsonValueKind.String)
                {
                    error = item + ": missing-kind";
                    return false;
                }

                if (!element.TryGetProperty("name", out JsonElement nameElement) ||
                    nameElement.ValueKind != JsonValueKind.String)
                {
                    error = item + ": missing-name";
                    return false;
                }

                string? kind = kindElement.GetString();
                string name = nameElement.GetString() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(name))
                {
                    error = item + ": empty-name";
                    return false;
                }

                if (string.Equals(kind, "system", StringComparison.Ordinal))
                {
                    if (!element.TryGetProperty("action", out JsonElement actionElement) ||
                        actionElement.ValueKind != JsonValueKind.String)
                    {
                        error = item + ": missing-action";
                        return false;
                    }

                    string? action = actionElement.GetString();
                    if (action is null || !KnownSystemActions.Contains(action))
                    {
                        error = item + ": unknown-action";
                        return false;
                    }

                    parsed.Add(new ShortcutDefinition
                    {
                        Kind = ShortcutKind.System,
                        Action = action,
                        Name = name
                    });
                }
                else if (string.Equals(kind, "app", StringComparison.Ordinal))
                {
                    if (!element.TryGetProperty("path", out JsonElement pathElement) ||
                        pathElement.ValueKind != JsonValueKind.String)
                    {
                        error = item + ": missing-path";
                        return false;
                    }

                    string? path = pathElement.GetString();
                    if (string.IsNullOrWhiteSpace(path))
                    {
                        error = item + ": empty-path";
                        return false;
                    }

                    parsed.Add(new ShortcutDefinition
                    {
                        Kind = ShortcutKind.App,
                        Path = path,
                        Name = name
                    });
                }
                else
                {
                    error = item + ": unknown-kind";
                    return false;
                }

                index++;
            }

            if (parsed.Count > MaxShortcuts)
            {
                error = "too-many:" + parsed.Count;
                return false;
            }

            shortcuts = parsed;
            return true;
        }
        catch (JsonException exception)
        {
            error = "JsonException: " + exception.Message;
            return false;
        }
        catch (Exception exception)
        {
            error = exception.GetType().Name + ": " + exception.Message;
            return false;
        }
    }

    /// <summary>
    /// 保存（临时文件 + 覆盖移动）。测试钩子存在时不写磁盘。
    /// 回落态第一次保存前先备份原文件；备份失败则拒绝保存并返回 false。
    /// 生产保存前另复制一份滚动备份（成功后只留最近 3 份），失败不阻塞保存。
    /// </summary>
    public bool Save()
    {
        if (_saveHook is not null)
        {
            _saveHook(_shortcuts);
            LastSaveFailure = null;
            return true;
        }

        if (!EnsureFallbackBackup())
        {
            return false;
        }

        // 每次生产保存前留一份滚动备份；备份失败不阻塞主保存。
        TryCreateRollingBackup();

        string tempPath = $"{_filePath}.{Environment.ProcessId}.tmp";

        try
        {
            _fileSystem.CreateDirectory(_directoryPath);
            _fileSystem.WriteAllBytes(tempPath, Serialize(_shortcuts));
            _fileSystem.Move(tempPath, _filePath, overwrite: true);
            PruneRollingBackups();
            LastSaveFailure = null;
            return true;
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"ShortcutStore.Save failed: {exception.Message}");
            LastSaveFailure = WriteFailed;

            try
            {
                if (_fileSystem.FileExists(tempPath))
                {
                    _fileSystem.Delete(tempPath);
                }
            }
            catch (Exception)
            {
            }

            return false;
        }
    }

    /// <summary>回落态首次写入前的原文件备份；成功后同一次加载内不再重复备份。</summary>
    private bool EnsureFallbackBackup()
    {
        if (!_isFallback || _fallbackBackedUp)
        {
            return true;
        }

        try
        {
            string backupPath = BuildBackupPath();
            _fileSystem.Copy(_filePath, backupPath);
            _fallbackBackedUp = true;
            LastSaveFailure = null;
            return true;
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"ShortcutStore fallback backup failed: {exception.Message}");
            LastSaveFailure = FallbackBackupFailed;
            return false;
        }
    }

    private string BuildBackupPath()
    {
        string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        string candidate = Path.Combine(_directoryPath, $"shortcuts.backup-{stamp}.json");

        if (!_fileSystem.FileExists(candidate))
        {
            return candidate;
        }

        for (int index = 1; index < 1000; index++)
        {
            candidate = Path.Combine(_directoryPath, $"shortcuts.backup-{stamp}-{index}.json");
            if (!_fileSystem.FileExists(candidate))
            {
                return candidate;
            }
        }

        return Path.Combine(_directoryPath, $"shortcuts.backup-{stamp}-{Guid.NewGuid():N}.json");
    }

    /// <summary>
    /// 生产保存前的滚动备份：只复制当前主文件，失败仅记调试日志，不阻塞主保存。
    /// 与回落备份（<c>shortcuts.backup-*.json</c>，失败拒绝保存）互不影响。
    /// </summary>
    private void TryCreateRollingBackup()
    {
        try
        {
            if (!_fileSystem.FileExists(_filePath))
            {
                return;
            }

            _fileSystem.Copy(_filePath, BuildRollingBackupPath());
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"ShortcutStore rolling backup skipped: {exception.Message}");
        }
    }

    private string BuildRollingBackupPath()
    {
        string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
        int sequence = Interlocked.Increment(ref _rollingBackupSequence);
        string candidate = Path.Combine(
            _directoryPath,
            $"{RollingBackupPrefix}{stamp}-{sequence:D4}.json");

        if (!_fileSystem.FileExists(candidate))
        {
            return candidate;
        }

        for (int index = 1; index < 1000; index++)
        {
            string alternate = Path.Combine(
                _directoryPath,
                $"{RollingBackupPrefix}{stamp}-{sequence:D4}-{index}.json");
            if (!_fileSystem.FileExists(alternate))
            {
                return alternate;
            }
        }

        return Path.Combine(
            _directoryPath,
            $"{RollingBackupPrefix}{stamp}-{sequence:D4}-{Guid.NewGuid():N}.json");
    }

    /// <summary>
    /// 主保存成功后只保留最近 <see cref="RollingBackupsToKeep"/> 份滚动备份。
    /// 文件名含时间戳，按名称倒序即时间倒序；删除失败逐条吞掉，不影响保存结果。
    /// </summary>
    private void PruneRollingBackups()
    {
        try
        {
            List<string> backups = _fileSystem
                .EnumerateFiles(_directoryPath, RollingBackupPrefix + "*.json")
                .OrderByDescending(path => Path.GetFileName(path), StringComparer.Ordinal)
                .ToList();

            foreach (string stale in backups.Skip(RollingBackupsToKeep))
            {
                try
                {
                    _fileSystem.Delete(stale);
                }
                catch (Exception exception)
                {
                    Debug.WriteLine($"ShortcutStore rolling backup prune skipped: {exception.Message}");
                }
            }
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"ShortcutStore rolling backup prune failed: {exception.Message}");
        }
    }

    /// <summary>序列化为与 <see cref="Save"/> 完全一致的 JSON；internal 便于单测 round-trip。</summary>
    internal static byte[] Serialize(IReadOnlyList<ShortcutDefinition> shortcuts)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteStartArray("shortcuts");

            foreach (ShortcutDefinition shortcut in shortcuts)
            {
                writer.WriteStartObject();
                writer.WriteString("kind", shortcut.Kind == ShortcutKind.System ? "system" : "app");

                if (shortcut.Kind == ShortcutKind.System)
                {
                    writer.WriteString("action", shortcut.Action);
                }
                else
                {
                    writer.WriteString("path", shortcut.Path);
                }

                writer.WriteString("name", shortcut.Name);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return stream.ToArray();
    }
}
