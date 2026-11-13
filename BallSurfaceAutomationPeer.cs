using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;

namespace AssistantTouch;

/// <summary>
/// 悬浮球根元素的 <see cref="Grid"/> 子类：唯一目的是挂 automation peer。
/// 球的视觉、拖拽、位置记忆、右键菜单全部继承 Grid/窗口既有实现，行为零改动。
/// public 仅为满足 XAML 实例化（其它成员保持 internal）。
/// </summary>
public sealed class BallSurfaceElement : Grid
{
    /// <summary>Invoke 语义 = 未拖动的左键单击（打开径向菜单）；未接线时返回 false。</summary>
    internal Func<bool>? TryInvoke { get; set; }

    protected override AutomationPeer OnCreateAutomationPeer() =>
        new BallSurfaceAutomationPeer(this, () => TryInvoke?.Invoke() ?? false, () => IsVisible);
}

/// <summary>
/// 悬浮球的 UIA peer：让读屏能发现「助手触摸」并激活它（否则整个球在 UIA 上不可见，
/// 读屏用户无从进入径向菜单 — rev-18-prep-uia §3.4）。
/// </summary>
internal sealed class BallSurfaceAutomationPeer : FrameworkElementAutomationPeer, IInvokeProvider
{
    private readonly Func<bool> _tryInvoke;
    private readonly Func<bool> _isVisible;

    internal BallSurfaceAutomationPeer(FrameworkElement owner, Func<bool> tryInvoke, Func<bool> isVisible)
        : base(owner)
    {
        _tryInvoke = tryInvoke;
        _isVisible = isVisible;
    }

    protected override string GetClassNameCore() => "BallSurface";

    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Button;

    protected override string GetHelpTextCore()
    {
        string help = base.GetHelpTextCore();
        return string.IsNullOrWhiteSpace(help)
            ? "左键单击打开快捷菜单；拖动可移动；右键更多操作"
            : help;
    }

    /// <summary>窗口隐藏/最小化时不要让读屏仍把球报成可操作。</summary>
    protected override bool IsOffscreenCore() => !_isVisible() || base.IsOffscreenCore();

    public override object? GetPattern(PatternInterface patternInterface) =>
        patternInterface == PatternInterface.Invoke ? this : base.GetPattern(patternInterface);

    public void Invoke()
    {
        if (!_isVisible())
        {
            return;
        }

        _tryInvoke();
    }
}
