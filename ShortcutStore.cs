using System.IO;
using System.Text.Json;

namespace AssistantTouch;

/// <summary>
/// 快捷方式列表的加载/保存：<c>%LOCALAPPDATA%\AssistantTouch\shortcuts.json</c>。
/// 文件缺失、损坏、字段非法或超过 8 个时回落到默认 6 个系统快捷方式，不崩溃。
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

    public IReadOnlyList<ShortcutDefinition> Shortcuts => _shortcuts;

    public static ShortcutStore Load()
    {
        try
        {
            if (File.Exists(FilePath) &&
                TryParse(File.ReadAllText(FilePath), out List<ShortcutDefinition>? parsed) &&
                parsed is not null)
            {
                return new ShortcutStore(parsed);
            }
        }
        catch (Exception)
        {
            // 读失败按缺失处理。
        }

        return new ShortcutStore(ShortcutDefinition.CreateDefaults());
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
        shortcuts = null;

        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("shortcuts", out JsonElement array) ||
                array.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            var parsed = new List<ShortcutDefinition>();

            foreach (JsonElement element in array.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.Object ||
                    !element.TryGetProperty("kind", out JsonElement kindElement) ||
                    kindElement.ValueKind != JsonValueKind.String ||
                    !element.TryGetProperty("name", out JsonElement nameElement) ||
                    nameElement.ValueKind != JsonValueKind.String)
                {
                    return false;
                }

                string? kind = kindElement.GetString();
                string name = nameElement.GetString() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(name))
                {
                    return false;
                }

                if (string.Equals(kind, "system", StringComparison.Ordinal))
                {
                    if (!element.TryGetProperty("action", out JsonElement actionElement) ||
                        actionElement.ValueKind != JsonValueKind.String)
                    {
                        return false;
                    }

                    string? action = actionElement.GetString();
                    if (action is null || !KnownSystemActions.Contains(action))
                    {
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
                        return false;
                    }

                    string? path = pathElement.GetString();
                    if (string.IsNullOrWhiteSpace(path))
                    {
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
                    return false;
                }
            }

            if (parsed.Count > MaxShortcuts)
            {
                return false;
            }

            shortcuts = parsed;
            return true;
        }
        catch (Exception)
        {
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

            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
            {
                writer.WriteStartObject();
                writer.WriteStartArray("shortcuts");

                foreach (ShortcutDefinition shortcut in _shortcuts)
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

            File.WriteAllBytes(tempPath, stream.ToArray());
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
}
