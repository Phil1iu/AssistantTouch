using System.IO;
using Xunit;

namespace AssistantTouch.Tests;

/// <summary>
/// D74-A 回归：任何走 <see cref="ShortcutStore.CreateForTests"/> 的测试都不得触碰真实
/// <c>%LOCALAPPDATA%\AssistantTouch</c>（15:21 配置覆盖事故的防线）。
/// </summary>
public class ShortcutStoreIsolationTests
{
    [Fact]
    public void Tests_DoNotTouchRealUserConfig()
    {
        string realFile = ShortcutStore.FilePath;
        string realLog = ShortcutStore.LogFilePath;
        string realDirectory = Path.GetDirectoryName(realFile)!;

        bool fileExisted = File.Exists(realFile);
        byte[]? fileBefore = fileExisted ? File.ReadAllBytes(realFile) : null;
        DateTime? writeBefore = fileExisted ? File.GetLastWriteTimeUtc(realFile) : null;
        bool directoryExisted = Directory.Exists(realDirectory);
        Dictionary<string, (long Length, DateTime WriteTime)> directoryBefore =
            SnapshotDirectory(realDirectory);

        // 不传 saveHook：走默认 no-op 钩子；即使钩子被绕过，内存文件系统也不落盘。
        ShortcutStore store = ShortcutStore.CreateForTests(ShortcutDefinition.CreateDefaults());

        var app = new ShortcutDefinition
        {
            Kind = ShortcutKind.App,
            Path = @"C:\isolation\app.exe",
            Name = "隔离测试"
        };

        Assert.True(store.TryAdd(app));
        Assert.True(store.Rename(store.Shortcuts[0], "重命名后"));
        Assert.True(store.Move(store.Shortcuts[0], 1));
        Assert.True(store.Swap(store.Shortcuts[0], store.Shortcuts[1]));
        Assert.True(store.Remove(store.Shortcuts[0]));
        Assert.True(store.Replace(store.Shortcuts[0], app));
        Assert.True(store.ResetToDefaults());
        Assert.True(store.Save());

        // 真实主配置文件：存在性、字节内容、写入时间全部不变。
        Assert.Equal(fileExisted, File.Exists(realFile));
        if (fileExisted)
        {
            Assert.Equal(fileBefore!, File.ReadAllBytes(realFile));
            Assert.Equal(writeBefore, File.GetLastWriteTimeUtc(realFile));
        }

        // 真实配置目录：若存在则文件清单/大小/写入时间完全一致；若不存在则仍不存在。
        Assert.Equal(directoryExisted, Directory.Exists(realDirectory));
        Assert.Equal(directoryBefore, SnapshotDirectory(realDirectory));

        // 诊断日志同样不得被测试写入。
        Assert.False(File.Exists(realLog) && !directoryBefore.ContainsKey(Path.GetFileName(realLog)));
    }

    private static Dictionary<string, (long Length, DateTime WriteTime)> SnapshotDirectory(string directory)
    {
        var snapshot = new Dictionary<string, (long, DateTime)>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(directory))
        {
            return snapshot;
        }

        foreach (string path in Directory.EnumerateFiles(directory))
        {
            var info = new FileInfo(path);
            snapshot[info.Name] = (info.Length, info.LastWriteTimeUtc);
        }

        return snapshot;
    }
}
