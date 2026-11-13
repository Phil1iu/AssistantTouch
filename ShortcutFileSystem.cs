using System.IO;
using System.Text;

namespace AssistantTouch;

/// <summary>快捷方式配置文件的最小 IO 抽象（便于注入失败场景做单测）。</summary>
internal interface IShortcutFileSystem
{
    bool FileExists(string path);
    string ReadAllText(string path);
    void WriteAllBytes(string path, byte[] bytes);
    void WriteAllText(string path, string text);
    void Move(string source, string destination, bool overwrite);
    void Copy(string source, string destination);
    void Delete(string path);
    void CreateDirectory(string path);
    IEnumerable<string> EnumerateFiles(string directory, string searchPattern);
}

/// <summary>生产实现：直接转发到 System.IO。</summary>
internal sealed class ShortcutFileSystem : IShortcutFileSystem
{
    public bool FileExists(string path) => File.Exists(path);

    public string ReadAllText(string path) => File.ReadAllText(path);

    public void WriteAllBytes(string path, byte[] bytes) => File.WriteAllBytes(path, bytes);

    public void WriteAllText(string path, string text) => File.WriteAllText(path, text);

    public void Move(string source, string destination, bool overwrite) =>
        File.Move(source, destination, overwrite);

    public void Copy(string source, string destination) =>
        File.Copy(source, destination, overwrite: false);

    public void Delete(string path) => File.Delete(path);

    public void CreateDirectory(string path) => Directory.CreateDirectory(path);

    public IEnumerable<string> EnumerateFiles(string directory, string searchPattern) =>
        Directory.EnumerateFiles(directory, searchPattern);
}

/// <summary>
/// 内存文件系统：测试用，保证任何编辑/保存都不会落到真实磁盘或 %LOCALAPPDATA%。
/// </summary>
internal sealed class InMemoryShortcutFileSystem : IShortcutFileSystem
{
    private readonly Dictionary<string, byte[]> _files = new(StringComparer.OrdinalIgnoreCase);

    public bool FileExists(string path) => _files.ContainsKey(path);

    public string ReadAllText(string path) => Encoding.UTF8.GetString(_files[path]);

    public void WriteAllBytes(string path, byte[] bytes) => _files[path] = bytes.ToArray();

    public void WriteAllText(string path, string text) =>
        WriteAllBytes(path, Encoding.UTF8.GetBytes(text));

    public void Move(string source, string destination, bool overwrite)
    {
        if (!_files.TryGetValue(source, out byte[]? bytes))
        {
            throw new FileNotFoundException(source);
        }

        if (!overwrite && _files.ContainsKey(destination))
        {
            throw new IOException("destination exists");
        }

        _files[destination] = bytes;
        _files.Remove(source);
    }

    public void Copy(string source, string destination)
    {
        if (!_files.TryGetValue(source, out byte[]? bytes))
        {
            throw new FileNotFoundException(source);
        }

        _files[destination] = bytes.ToArray();
    }

    public void Delete(string path) => _files.Remove(path);

    public void CreateDirectory(string path)
    {
    }

    public IEnumerable<string> EnumerateFiles(string directory, string searchPattern) =>
        _files.Keys.Where(path =>
            string.Equals(Path.GetDirectoryName(path), directory, StringComparison.OrdinalIgnoreCase) &&
            MatchesFileName(Path.GetFileName(path), searchPattern));

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
}
