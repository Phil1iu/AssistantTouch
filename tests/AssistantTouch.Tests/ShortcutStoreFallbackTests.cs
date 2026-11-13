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
        // P2-C1：回落备份成功后主保存失败，标志必须复位，下次保存重新备份当前原文。
        Assert.False(store.FallbackBackupCreated);
    }

    [Fact]
    public void Save_BackupThenWriteFailure_BacksUpRestoredOriginalOnNextSave()
    {
        (ShortcutStore store, FakeShortcutFileSystem fileSystem) = LoadBroken();
        fileSystem.FailWrite = true;

        Assert.False(store.Save());
        Assert.Equal(1, fileSystem.CopyCount);
        Assert.False(store.FallbackBackupCreated);

        // 用户手工把恢复后的原文写回（模拟「抄回备份」），应用未重启。
        fileSystem.FailWrite = false;
        const string restored = "{\"shortcuts\":[]}";
        fileSystem.Files[FilePath] = Encoding.UTF8.GetBytes(restored);

        Assert.True(store.Save());
        Assert.Equal(2, fileSystem.CopyCount);
        Assert.True(store.FallbackBackupCreated);

        string[] backups = fileSystem.BackupPaths();
        Assert.Equal(2, backups.Length);
        Assert.Contains(backups, path => Encoding.UTF8.GetString(fileSystem.Files[path]) == restored);
    }

    [Fact]
    public void Load_RemovesStaleTempFilesButKeepsCurrentProcessAndOtherFiles()
    {
        var fileSystem = new FakeShortcutFileSystem();
        fileSystem.Files[FilePath] = Encoding.UTF8.GetBytes("{\"shortcuts\":[]}");

        string stale = FilePath + ".12345.tmp";
        string currentProcess = FilePath + "." + Environment.ProcessId + ".tmp";
        string notProcessId = FilePath + ".notpid.tmp";
        string otherFile = @"C:\fake\other.json.999.tmp";

        foreach (string path in new[] { stale, currentProcess, notProcessId, otherFile })
        {
            fileSystem.Files[path] = new byte[] { 1 };
        }

        ShortcutStore store = ShortcutStore.Load(FilePath, LogPath, fileSystem);

        Assert.Empty(store.Shortcuts);
        Assert.False(fileSystem.Files.ContainsKey(stale));
        Assert.True(fileSystem.Files.ContainsKey(currentProcess));
        Assert.True(fileSystem.Files.ContainsKey(notProcessId));
        Assert.True(fileSystem.Files.ContainsKey(otherFile));
    }

    [Fact]
    public void Load_StaleTempDeleteFailure_DoesNotThrow()
    {
        var fileSystem = new FakeShortcutFileSystem { FailDelete = true };
        fileSystem.Files[FilePath] = Encoding.UTF8.GetBytes("{\"shortcuts\":[]}");
        string stale = FilePath + ".12345.tmp";
        fileSystem.Files[stale] = new byte[] { 1 };

        ShortcutStore store = ShortcutStore.Load(FilePath, LogPath, fileSystem);

        Assert.NotNull(store);
        Assert.True(fileSystem.Files.ContainsKey(stale));
    }
}

/// <summary>内存文件系统：可注入读/写/复制失败，记录备份文件；回落备份与滚动备份分别计次。</summary>
internal sealed class FakeShortcutFileSystem : IShortcutFileSystem
{
    public Dictionary<string, byte[]> Files { get; } = new(StringComparer.OrdinalIgnoreCase);

    public bool FailRead { get; set; }
    public bool FailWrite { get; set; }
    public bool FailCopy { get; set; }
    public bool FailMove { get; set; }
    public bool FailDelete { get; set; }

    /// <summary>只让滚动备份（<c>shortcuts.bak-*</c>）复制失败。</summary>
    public bool FailRollingCopy { get; set; }

    /// <summary>回落备份复制次数（既有断言依赖此语义）。</summary>
    public int CopyCount { get; private set; }

    /// <summary>滚动备份复制次数。</summary>
    public int RollingCopyCount { get; private set; }

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
        bool rolling = Path.GetFileName(destination)
            .StartsWith(ShortcutStore.RollingBackupPrefix, StringComparison.OrdinalIgnoreCase);

        if (rolling)
        {
            RollingCopyCount++;

            if (FailRollingCopy)
            {
                throw new IOException("simulated rolling backup failure");
            }
        }
        else
        {
            CopyCount++;

            if (FailCopy)
            {
                throw new IOException("simulated copy failure");
            }
        }

        if (!Files.TryGetValue(source, out byte[]? bytes))
        {
            throw new FileNotFoundException(source);
        }

        Files[destination] = bytes.ToArray();
    }

    public void Delete(string path)
    {
        if (FailDelete)
        {
            throw new IOException("simulated delete failure");
        }

        Files.Remove(path);
    }

    public void CreateDirectory(string path)
    {
    }

    public IEnumerable<string> EnumerateFiles(string directory, string searchPattern) =>
        Files.Keys
            .Where(path =>
                string.Equals(Path.GetDirectoryName(path), directory, StringComparison.OrdinalIgnoreCase) &&
                MatchesFileName(Path.GetFileName(path), searchPattern))
            .ToArray();

    private static bool MatchesFileName(string fileName, string pattern)
    {
        int star = pattern.IndexOf('*');
        if (star < 0)
        {
            return string.Equals(fileName, pattern, StringComparison.OrdinalIgnoreCase);
        }

        string prefix = pattern[..star];
        string suffix = pattern[(star + 1)..];

        return fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
               fileName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase);
    }

    public string[] BackupPaths() =>
        Files.Keys
            .Where(path => path.Contains("shortcuts.backup-", StringComparison.OrdinalIgnoreCase))
            .ToArray();

    public string[] RollingBackupPaths() =>
        Files.Keys
            .Where(path => Path.GetFileName(path)
                .StartsWith(ShortcutStore.RollingBackupPrefix, StringComparison.OrdinalIgnoreCase))
            .ToArray();
}
