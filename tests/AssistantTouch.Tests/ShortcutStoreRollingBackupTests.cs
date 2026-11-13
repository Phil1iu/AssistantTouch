using System.IO;
using System.Text;
using Xunit;

namespace AssistantTouch.Tests;

/// <summary>
/// D74-B：生产保存前的滚动备份（<c>shortcuts.bak-*.json</c>）。
/// 每次保存前复制当前主文件，成功后只保留最近 3 份；备份失败不阻塞主保存。
/// </summary>
public class ShortcutStoreRollingBackupTests
{
    private const string FilePath = @"C:\fake\shortcuts.json";
    private const string LogPath = @"C:\fake\shortcuts-load.log";

    private static ShortcutStore LoadExisting(FakeShortcutFileSystem fileSystem)
    {
        fileSystem.Files[FilePath] = ShortcutStore.Serialize(ShortcutDefinition.CreateDefaults());
        return ShortcutStore.Load(FilePath, LogPath, fileSystem);
    }

    private static int[] Sequences(FakeShortcutFileSystem fileSystem)
    {
        return fileSystem.RollingBackupPaths()
            .Select(path =>
            {
                string name = Path.GetFileNameWithoutExtension(path);
                int dash = name.LastIndexOf('-');
                return int.Parse(name[(dash + 1)..]);
            })
            .OrderBy(value => value)
            .ToArray();
    }

    [Fact]
    public void Save_FirstRunWithoutFile_DoesNotCreateRollingBackup()
    {
        var fileSystem = new FakeShortcutFileSystem();
        ShortcutStore store = ShortcutStore.Load(FilePath, LogPath, fileSystem);

        Assert.True(store.Save());

        Assert.Equal(0, fileSystem.RollingCopyCount);
        Assert.Empty(fileSystem.RollingBackupPaths());
    }

    [Fact]
    public void Save_WritesPreviousContentIntoRollingBackup()
    {
        var fileSystem = new FakeShortcutFileSystem();
        ShortcutStore store = LoadExisting(fileSystem);
        byte[] original = fileSystem.Files[FilePath].ToArray();

        var app = new ShortcutDefinition
        {
            Kind = ShortcutKind.App,
            Path = @"C:\app.exe",
            Name = "app"
        };

        Assert.True(store.TryAdd(app));

        string backup = Assert.Single(fileSystem.RollingBackupPaths());
        Assert.Equal(original, fileSystem.Files[backup]);
        Assert.True(ShortcutStore.TryParse(Encoding.UTF8.GetString(fileSystem.Files[FilePath]), out List<ShortcutDefinition>? parsed));
        Assert.Equal(7, parsed!.Count);
        // 滚动备份不是回落备份，不占用回落备份的计数。
        Assert.Empty(fileSystem.BackupPaths());
    }

    [Fact]
    public void Save_FiveTimes_KeepsOnlyThreeNewestRollingBackups()
    {
        var fileSystem = new FakeShortcutFileSystem();
        ShortcutStore store = LoadExisting(fileSystem);

        for (int index = 0; index < 5; index++)
        {
            Assert.True(store.Save());
        }

        Assert.Equal(5, fileSystem.RollingCopyCount);
        Assert.Equal(new[] { 3, 4, 5 }, Sequences(fileSystem));
    }

    [Fact]
    public void Save_RollingBackupFailure_DoesNotBlockSaveAndIsNotReported()
    {
        var fileSystem = new FakeShortcutFileSystem();
        ShortcutStore store = LoadExisting(fileSystem);
        fileSystem.FailRollingCopy = true;

        var app = new ShortcutDefinition
        {
            Kind = ShortcutKind.App,
            Path = @"C:\app.exe",
            Name = "app"
        };

        Assert.True(store.TryAdd(app));
        Assert.Null(store.LastSaveFailure);
        Assert.Empty(fileSystem.RollingBackupPaths());
        Assert.True(ShortcutStore.TryParse(Encoding.UTF8.GetString(fileSystem.Files[FilePath]), out List<ShortcutDefinition>? parsed));
        Assert.Equal(7, parsed!.Count);
    }

    [Fact]
    public void Save_PruneKeepsFallbackBackups()
    {
        var fileSystem = new FakeShortcutFileSystem();
        fileSystem.Files[FilePath] = Encoding.UTF8.GetBytes("{bad json");
        ShortcutStore store = ShortcutStore.Load(FilePath, LogPath, fileSystem);

        for (int index = 0; index < 5; index++)
        {
            Assert.True(store.Save());
        }

        // 回落备份只有首次保存前那一份，滚动备份清理不得顺带删除它。
        Assert.Single(fileSystem.BackupPaths());
        Assert.Equal(3, fileSystem.RollingBackupPaths().Length);
    }
}
