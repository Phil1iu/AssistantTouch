using System.IO;
using System.Text;
using Xunit;

namespace AssistantTouch.Tests;

/// <summary>配置读失败回落保护（P2-S1）：备份、拒绝保存、缺失文件不备份。</summary>
public class ShortcutStoreFallbackTests
{
    private const string FilePath = @"C:\fake\shortcuts.json";
    private const string LogPath = @"C:\fake\shortcuts-load.log";
    private const string BrokenJson = "{bad json";

    private static (ShortcutStore Store, FakeShortcutFileSystem FileSystem) LoadBroken()
    {
        var fileSystem = new FakeShortcutFileSystem();
        fileSystem.Files[FilePath] = Encoding.UTF8.GetBytes(BrokenJson);
        ShortcutStore store = ShortcutStore.Load(FilePath, LogPath, fileSystem);
        return (store, fileSystem);
    }

    [Fact]
    public void Load_ParseFailure_MarksFallbackAndKeepsOriginalFile()
    {
        (ShortcutStore store, FakeShortcutFileSystem fileSystem) = LoadBroken();

        Assert.True(store.IsFallback);
        Assert.False(string.IsNullOrWhiteSpace(store.FallbackReason));
        Assert.Equal(6, store.Shortcuts.Count);
        Assert.Equal(BrokenJson, Encoding.UTF8.GetString(fileSystem.Files[FilePath]));

        string log = Encoding.UTF8.GetString(fileSystem.Files[LogPath]);
        Assert.Contains("fallback=true", log);
    }

    [Fact]
    public void Load_MissingFile_IsNotFallbackAndSaveDoesNotBackup()
    {
        var fileSystem = new FakeShortcutFileSystem();

        ShortcutStore store = ShortcutStore.Load(FilePath, LogPath, fileSystem);

        Assert.False(store.IsFallback);
        Assert.Null(store.FallbackReason);
        Assert.Equal(6, store.Shortcuts.Count);

        Assert.True(store.Save());
        Assert.False(store.FallbackBackupCreated);
        Assert.Equal(0, fileSystem.CopyCount);
        Assert.Empty(fileSystem.BackupPaths());
        Assert.True(fileSystem.Files.ContainsKey(FilePath));
        Assert.Contains("fallback=false", Encoding.UTF8.GetString(fileSystem.Files[LogPath]));
    }

    [Fact]
    public void Load_ReadException_MarksFallback()
    {
        var fileSystem = new FakeShortcutFileSystem { FailRead = true };
        fileSystem.Files[FilePath] = Encoding.UTF8.GetBytes(BrokenJson);

        ShortcutStore store = ShortcutStore.Load(FilePath, LogPath, fileSystem);

        Assert.True(store.IsFallback);
        Assert.NotNull(store.FallbackReason);
        Assert.Contains("IOException", store.FallbackReason);
    }

    [Fact]
    public void Save_FallbackFirstSave_BacksUpOriginalOnce()
    {
        (ShortcutStore store, FakeShortcutFileSystem fileSystem) = LoadBroken();

        Assert.True(store.Save());
        Assert.True(store.FallbackBackupCreated);

        string backup = Assert.Single(fileSystem.BackupPaths());
        Assert.Equal(BrokenJson, Encoding.UTF8.GetString(fileSystem.Files[backup]));
        Assert.True(ShortcutStore.TryParse(Encoding.UTF8.GetString(fileSystem.Files[FilePath]), out _));

        // 后续保存不再重复备份。
        Assert.True(store.Save());
        Assert.Equal(1, fileSystem.CopyCount);
        Assert.Single(fileSystem.BackupPaths());
    }

    [Fact]
    public void Save_BackupFailure_BlocksSaveAndEdit()
    {
        (ShortcutStore store, FakeShortcutFileSystem fileSystem) = LoadBroken();
        fileSystem.FailCopy = true;

        Assert.False(store.Save());
        Assert.Equal(ShortcutStore.FallbackBackupFailed, store.LastSaveFailure);
        Assert.Empty(fileSystem.BackupPaths());
        Assert.Equal(BrokenJson, Encoding.UTF8.GetString(fileSystem.Files[FilePath]));

        var app = new ShortcutDefinition
        {
            Kind = ShortcutKind.App,
            Path = @"C:\app.exe",
            Name = "app"
        };

        Assert.False(store.TryAdd(app));
        Assert.Equal(6, store.Shortcuts.Count);
        Assert.Equal(ShortcutStore.FallbackBackupFailed, store.LastSaveFailure);
    }

    [Fact]
    public void Edit_InFallback_BacksUpThenSavesAndDoesNotRepeatBackup()
    {
        (ShortcutStore store, FakeShortcutFileSystem fileSystem) = LoadBroken();
        var app = new ShortcutDefinition
        {
            Kind = ShortcutKind.App,
            Path = @"C:\app.exe",
            Name = "app"
        };

        Assert.True(store.TryAdd(app));
        Assert.Equal(1, fileSystem.CopyCount);

        string backup = Assert.Single(fileSystem.BackupPaths());
        Assert.Equal(BrokenJson, Encoding.UTF8.GetString(fileSystem.Files[backup]));

        Assert.True(ShortcutStore.TryParse(Encoding.UTF8.GetString(fileSystem.Files[FilePath]), out List<ShortcutDefinition>? parsed));
        Assert.Equal(7, parsed!.Count);

        Assert.True(store.Remove(app));
        Assert.Equal(1, fileSystem.CopyCount);
        Assert.Single(fileSystem.BackupPaths());
    }

    [Fact]
    public void Save_WriteFailure_ReportsWriteFailedAfterBackup()
    {
        (ShortcutStore store, FakeShortcutFileSystem fileSystem) = LoadBroken();
        fileSystem.FailWrite = true;

        Assert.False(store.Save());
        Assert.Equal(ShortcutStore.WriteFailed, store.LastSaveFailure);
        Assert.Single(fileSystem.BackupPaths());
    }
}

/// <summary>内存文件系统：可注入读/写/复制失败，记录备份文件。</summary>
internal sealed class FakeShortcutFileSystem : IShortcutFileSystem
{
    public Dictionary<string, byte[]> Files { get; } = new(StringComparer.OrdinalIgnoreCase);

    public bool FailRead { get; set; }
    public bool FailWrite { get; set; }
    public bool FailCopy { get; set; }
    public bool FailMove { get; set; }

    public int CopyCount { get; private set; }

    public bool FileExists(string path) => Files.ContainsKey(path);

    public string ReadAllText(string path)
    {
        if (FailRead)
        {
            throw new IOException("simulated read failure");
        }

        if (!Files.TryGetValue(path, out byte[]? bytes))
        {
            throw new FileNotFoundException(path);
        }

        return Encoding.UTF8.GetString(bytes);
    }

    public void WriteAllBytes(string path, byte[] bytes)
    {
        if (FailWrite)
        {
            throw new IOException("simulated write failure");
        }

        Files[path] = bytes.ToArray();
    }

    public void WriteAllText(string path, string text) =>
        WriteAllBytes(path, Encoding.UTF8.GetBytes(text));

    public void Move(string source, string destination, bool overwrite)
    {
        if (FailMove)
        {
            throw new IOException("simulated move failure");
        }

        if (!Files.TryGetValue(source, out byte[]? bytes))
        {
            throw new FileNotFoundException(source);
        }

        if (!overwrite && Files.ContainsKey(destination))
        {
            throw new IOException("destination exists");
        }

        Files[destination] = bytes;
        Files.Remove(source);
    }

    public void Copy(string source, string destination)
    {
        CopyCount++;

        if (FailCopy)
        {
            throw new IOException("simulated copy failure");
        }

        if (!Files.TryGetValue(source, out byte[]? bytes))
        {
            throw new FileNotFoundException(source);
        }

        Files[destination] = bytes.ToArray();
    }

    public void Delete(string path) => Files.Remove(path);

    public void CreateDirectory(string path)
    {
    }

    public string[] BackupPaths() =>
        Files.Keys
            .Where(path => path.Contains("shortcuts.backup-", StringComparison.OrdinalIgnoreCase))
            .ToArray();
}
