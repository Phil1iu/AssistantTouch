using System.IO;

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
}
