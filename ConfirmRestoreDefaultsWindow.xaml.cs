using System.Windows;
using System.Windows.Input;

namespace AssistantTouch;

/// <summary>
/// 「恢复默认」二次确认对话框。默认焦点在「取消」，Esc 取消；只有点「恢复」才返回 true。
/// </summary>
public partial class ConfirmRestoreDefaultsWindow : Window
{
    internal ConfirmRestoreDefaultsWindow()
    {
        InitializeComponent();

        Loaded += OnLoaded;
        PreviewKeyDown += OnPreviewKeyDown;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // 防误触：默认焦点放在取消上。
        CancelButton.Focus();
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            DialogResult = false;
            e.Handled = true;
        }
    }

    private void OnConfirmClick(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }
}
