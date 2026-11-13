using System.IO;
using System.Text;
using Xunit;

namespace AssistantTouch.Tests;

/// <summary>
/// D74-C（rev-16 P2-N1）：保存写盘失败时编辑方法必须回滚内存列表，
/// 断言返回 false、列表逐项一致、<see cref="ShortcutStore.LastSaveFailure"/> 有值，
/// 解除注入后可继续操作且落盘不含失败改动。
/// </summary>
public class ShortcutStoreRollbackTests
{
    private const string FilePath = @"C:\fake\shortcuts.json";
    private const string LogPath = @"C:\fake\shortcuts-load.log";

    private static ShortcutStore LoadExisting(FakeShortcutFileSystem fileSystem, IEnumerable<ShortcutDefinition>? items = null)
    {
        fileSystem.Files[FilePath] = ShortcutStore.Serialize((items ?? ShortcutDefinition.CreateDefaults()).ToList());
        return ShortcutStore.Load(FilePath, LogPath, fileSystem);
    }

    private static string[] Names(ShortcutStore store) => store.Shortcuts.Select(item => item.Name).ToArray();

    private static string[] DiskNames(FakeShortcutFileSystem fileSystem)
    {
        Assert.True(ShortcutStore.TryParse(
            Encoding.UTF8.GetString(fileSystem.Files[FilePath]),
            out List<ShortcutDefinition>? parsed));
        return parsed!.Select(item => item.Name).ToArray();
    }

    private static ShortcutDefinition App(string name = "app") => new()
    {
        Kind = ShortcutKind.App,
        Path = @"C:\app.exe",
        Name = name
    };

    [Fact]
    public void TryAdd_WriteFailure_RollsBackAndAllowsRetry()
    {
        var fileSystem = new FakeShortcutFileSystem();
        ShortcutStore store = LoadExisting(fileSystem);
        string[] before = Names(store);
        byte[] diskBefore = fileSystem.Files[FilePath].ToArray();
        fileSystem.FailWrite = true;

        Assert.False(store.TryAdd(App()));
        Assert.Equal(before, Names(store));
        Assert.Equal(ShortcutStore.WriteFailed, store.LastSaveFailure);
        Assert.Equal(diskBefore, fileSystem.Files[FilePath]);
        Assert.DoesNotContain(fileSystem.Files.Keys, path => path.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase));

        fileSystem.FailWrite = false;
        Assert.True(store.TryAdd(App()));
        Assert.Null(store.LastSaveFailure);
        Assert.Equal(before.Length + 1, store.Shortcuts.Count);
        Assert.Equal(Names(store), DiskNames(fileSystem));
    }

    [Fact]
    public void Move_MoveFailure_RollsBackOrder()
    {
        var fileSystem = new FakeShortcutFileSystem();
        ShortcutStore store = LoadExisting(fileSystem);
        string[] before = Names(store);
        byte[] diskBefore = fileSystem.Files[FilePath].ToArray();
        ShortcutDefinition first = store.Shortcuts[0];
        fileSystem.FailMove = true;

        Assert.False(store.Move(first, 1));
        Assert.Equal(before, Names(store));
        Assert.Equal(0, store.IndexOf(first));
        Assert.Equal(ShortcutStore.WriteFailed, store.LastSaveFailure);
        Assert.Equal(diskBefore, fileSystem.Files[FilePath]);
        Assert.DoesNotContain(fileSystem.Files.Keys, path => path.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase));

        fileSystem.FailMove = false;
        Assert.True(store.Move(first, 1));
        Assert.Equal(1, store.IndexOf(first));
        Assert.Equal(Names(store), DiskNames(fileSystem));
    }

    [Fact]
    public void Rename_MoveFailure_RollsBackName()
    {
        var fileSystem = new FakeShortcutFileSystem();
        ShortcutStore store = LoadExisting(fileSystem);
        ShortcutDefinition target = store.Shortcuts[0];
        string originalName = target.Name;
        fileSystem.FailMove = true;

        Assert.False(store.Rename(target, "新名字"));
        Assert.Equal(originalName, store.Shortcuts[0].Name);
        Assert.Equal(ShortcutStore.WriteFailed, store.LastSaveFailure);

        fileSystem.FailMove = false;
        Assert.True(store.Rename(target, "新名字"));
        Assert.Equal("新名字", store.Shortcuts[0].Name);
        Assert.Equal("新名字", DiskNames(fileSystem)[0]);
    }

    [Fact]
    public void Remove_WriteFailure_RollsBackList()
    {
        var fileSystem = new FakeShortcutFileSystem();
        ShortcutStore store = LoadExisting(fileSystem);
        string[] before = Names(store);
        ShortcutDefinition first = store.Shortcuts[0];
        fileSystem.FailWrite = true;

        Assert.False(store.Remove(first));
        Assert.Equal(before, Names(store));
        Assert.Equal(0, store.IndexOf(first));
        Assert.Equal(ShortcutStore.WriteFailed, store.LastSaveFailure);

        fileSystem.FailWrite = false;
        Assert.True(store.Remove(first));
        Assert.Equal(before.Length - 1, store.Shortcuts.Count);
        Assert.Equal(Names(store), DiskNames(fileSystem));
    }

    [Fact]
    public void ResetToDefaults_WriteFailure_RestoresPreviousList()
    {
        var fileSystem = new FakeShortcutFileSystem();
        var custom = new List<ShortcutDefinition> { App("甲"), App("乙") };
        ShortcutStore store = LoadExisting(fileSystem, custom);
        string[] before = Names(store);
        fileSystem.FailWrite = true;

        Assert.False(store.ResetToDefaults());
        Assert.Equal(before, Names(store));
        Assert.Equal(ShortcutStore.WriteFailed, store.LastSaveFailure);
        Assert.Equal(before, DiskNames(fileSystem));

        fileSystem.FailWrite = false;
        Assert.True(store.ResetToDefaults());
        Assert.Equal(ShortcutDefinition.CreateDefaults().Count, store.Shortcuts.Count);
        Assert.Equal(Names(store), DiskNames(fileSystem));
    }
}
