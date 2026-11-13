using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;

namespace AssistantTouch;

/// <summary>环上圆的类型：快捷项圆，或末位的「+」圆。</summary>
internal enum RadialSlotKind
{
    Shortcut,
    Plus
}

/// <summary>
/// 径向菜单小圆的 <see cref="Border"/> 薄子类：唯一目的是挂自定义 automation peer。
/// <para>
/// <see cref="Border"/> 的 <c>OnCreateAutomationPeer</c> 返回 null，所以当前 UIA 树里
/// 根本没有这些小圆；而挂 peer 必须覆写该方法（它是 protected，无法从外部挂载）。
/// </para>
/// <para>
/// 视觉、命中测试、<c>RenderTransform</c> 悬停缩放、<c>Tag</c>、鼠标事件全部继承
/// <see cref="Border"/>，布局与 D72 行为零改动。
/// </para>
/// </summary>
internal sealed class RadialCircle : Border
{
    internal RadialCircle(
        int slotIndex,
        RadialSlotKind kind,
        string nameFallback,
        Func<int, ActivationSource, bool> tryActivate,
        Func<bool> isVisible)
    {
        SlotIndex = slotIndex;
        Kind = kind;
        NameFallback = nameFallback;
        TryActivate = tryActivate;
        IsVisibleForAutomation = isVisible;
    }

    /// <summary>在 <c>_circleSlots</c> 中的当前序号（每次 <c>RebuildRing</c> 重建时重算）。</summary>
    internal int SlotIndex { get; }

    internal RadialSlotKind Kind { get; }

    /// <summary>tooltip 文案；peer 在 <c>AutomationProperties.Name</c> 为空时回落到它。</summary>
    internal string NameFallback { get; }

    /// <summary>统一激活入口（快捷项执行 / 「+」开菜单）。</summary>
    internal Func<int, ActivationSource, bool> TryActivate { get; }

    /// <summary>环窗口当前是否可见（对话框隐藏期间为 false）。</summary>
    internal Func<bool> IsVisibleForAutomation { get; }

    protected override AutomationPeer OnCreateAutomationPeer() =>
        new RadialCircleAutomationPeer(this);
}

/// <summary>
/// 小圆的 UIA peer：暴露 <see cref="AutomationControlType.Button"/> 与 Invoke 模式，
/// 让读屏能「列出圆 → 激活」。Invoke 语义 = 未拖动的左键单击：
/// 快捷项执行并关环，「+」在圆下方打开添加菜单。
/// MVP-1 不实现 Selection（rev-18-prep-uia §3.2）。
/// </summary>
internal sealed class RadialCircleAutomationPeer : FrameworkElementAutomationPeer, IInvokeProvider
{
    private readonly RadialCircle _circle;

    internal RadialCircleAutomationPeer(RadialCircle owner)
        : base(owner)
    {
        _circle = owner;
    }

    protected override string GetClassNameCore() => "RadialCircle";

    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Button;

    /// <summary>名称优先取 <c>AutomationProperties.Name</c>，为空时回落到 tooltip 文案。</summary>
    protected override string GetNameCore()
    {
        string name = base.GetNameCore();
        return string.IsNullOrWhiteSpace(name) ? _circle.NameFallback : name;
    }

    protected override string GetHelpTextCore()
    {
        string help = base.GetHelpTextCore();
        if (!string.IsNullOrWhiteSpace(help))
        {
            return help;
        }

        return _circle.Kind == RadialSlotKind.Plus
            ? "添加系统快捷或应用程序"
            : "按 Enter 执行；右键更多操作";
    }

    /// <summary>
    /// <c>HideRadialForDialog</c> 期间窗口 <c>IsVisible=false</c>，但圆自身的
    /// <c>Visibility</c> 仍是 Visible；基类不看窗口可见性，这里补上。
    /// </summary>
    protected override bool IsOffscreenCore()
    {
        if (!_circle.IsVisibleForAutomation())
        {
            return true;
        }

        return base.IsOffscreenCore();
    }

    /// <summary>Invoke 不抛异常（隐藏/拖拽竞态下静默忽略），由统一入口做守卫。</summary>
    public void Invoke()
    {
        if (!_circle.IsVisibleForAutomation())
        {
            return;
        }

        _circle.TryActivate(_circle.SlotIndex, ActivationSource.Uia);
    }
}
