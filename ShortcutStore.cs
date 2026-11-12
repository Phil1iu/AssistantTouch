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

    private static readonly HashSet<string> KnownSystemActions = new(StringComparer.Ordinal)
    {
        "showDesktop",
        "screenshot",
        "taskManager",
        "settings",
        "explorer",
        "lock"
    };

    private static readonly string DirectoryPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AssistantTouch");

    private readonly List<ShortcutDefinition> _shortcuts;

    private ShortcutStore(List<ShortcutDefinition> shortcuts)
    {
        _shortcuts = shortcuts;
    }

    /// <summary>快捷方式文件的完整路径。</summary>
    public static string FilePath { get; } = Path.Combine(DirectoryPath, "shortcuts.json");

    /// <summary>最近一次加载的诊断日志路径（覆盖写）。</summary>
    public static string LogFilePath { get; } = Path.Combine(DirectoryPath, "shortcuts-load.log");

    public IReadOnlyList<ShortcutDefinition> Shortcuts => _shortcuts;

    public static ShortcutStore Load()
    {
        string? text = null;

        try
        {
            if (File.Exists(FilePath))
            {
                text = File.ReadAllText(FilePath);
                if (TryParse(text, out List<ShortcutDefinition>? parsed, out string? error) && parsed is not null)
                {
                    WriteLog(exists: true, length: text.Length, parsed: true, count: parsed.Count, error: null, text: text);
                    return new ShortcutStore(parsed);
                }

                WriteLog(exists: true, length: text.Length, parsed: false, count: 0, error: error ?? "unknown", text: text);
            }
            else
            {
                WriteLog(exists: false, length: 0, parsed: false, count: 0, error: "missing", text: null);
            }
        }
        catch (Exception exception)
        {
            WriteLog(
                exists: File.Exists(FilePath),
                length: text?.Length ?? -1,
                parsed: false,
                count: 0,
                error: exception.GetType().Name + ": " + exception.Message,
                text: text);
        }

        return new ShortcutStore(ShortcutDefinition.CreateDefaults());
    }

    /// <summary>把本次加载结果覆盖写到 <c>shortcuts-load.log</c>；日志失败不影响加载。</summary>
    private static void WriteLog(bool exists, int length, bool parsed, int count, string? error, string? text)
    {
        try
        {
            Directory.CreateDirectory(DirectoryPath);

            string line = string.Join(
                " ",
                DateTime.Now.ToString("O"),
                "path=" + FilePath,
                "exists=" + exists,
                "length=" + length,
                "parsed=" + parsed,
                "count=" + count,
                "error=" + (error ?? "-"),
                "preview=" + BuildPreview(text));

            File.WriteAllText(LogFilePath, line + Environment.NewLine);
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
        if (_shortcuts.Count >= MaxShortcuts)
        {
            return false;
        }

        _shortcuts.Add(shortcut);
        Save();
        return true;
    }

    public bool Remove(ShortcutDefinition shortcut)
    {
        bool removed = _shortcuts.Remove(shortcut);
        if (removed)
        {
            Save();
        }

        return removed;
    }

    public bool Replace(ShortcutDefinition oldShortcut, ShortcutDefinition newShortcut)
    {
        int index = _shortcuts.IndexOf(oldShortcut);
        if (index < 0)
        {
            return false;
        }

        _shortcuts[index] = newShortcut;
        Save();
        return true;
    }

    public void ResetToDefaults()
    {
        _shortcuts.Clear();
        _shortcuts.AddRange(ShortcutDefinition.CreateDefaults());
        Save();
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

    /// <summary>保存（临时文件 + 覆盖移动）；失败静默忽略。</summary>
    public void Save()
    {
        string tempPath = $"{FilePath}.{Environment.ProcessId}.tmp";

        try
        {
            Directory.CreateDirectory(DirectoryPath);
            File.WriteAllBytes(tempPath, Serialize(_shortcuts));
            File.Move(tempPath, FilePath, overwrite: true);
        }
        catch (Exception)
        {
            try
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
            catch (Exception)
            {
            }
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
