using System.Windows;
using System.Windows.Input;

namespace AssistantTouch;

/// <summary>
/// 重命名快捷方式的小输入对话框：白底细边、TextBox + 确定/取消，Esc 取消。
/// 校验（去首尾空格、非空、≤20）不通过时提示并保持对话框打开。
/// </summary>
public partial class RenameShortcutWindow : Window
{
    /// <summary>校验通过后的规范化名称。</summary>
    public string ResultName { get; private set; } = string.Empty;

    internal RenameShortcutWindow(string initialName)
    {
        InitializeComponent();

        NameBox.Text = initialName;
        Loaded += OnLoaded;
        PreviewKeyDown += OnPreviewKeyDown;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        NameBox.Focus();
        NameBox.SelectAll();
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            DialogResult = false;
            e.Handled = true;
        }
    }

    private void OnOkClick(object sender, RoutedEventArgs e)
    {
        if (!ShortcutName.TryNormalize(NameBox.Text, out string normalized, out string error))
        {
            MessageBox.Show(this, error, "重命名快捷方式", MessageBoxButton.OK, MessageBoxImage.Warning);
            NameBox.Focus();
            NameBox.SelectAll();
            return;
        }

        ResultName = normalized;
        DialogResult = true;
    }
}
