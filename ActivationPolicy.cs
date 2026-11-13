namespace AssistantTouch;

/// <summary>激活入口的调用来源。</summary>
internal enum ActivationSource
{
    /// <summary>键盘 Enter/Space：需先按过方向键进入选中态（保持旧语义）。</summary>
    Keyboard,

    /// <summary>鼠标未拖动的左键单击。</summary>
    Mouse,

    /// <summary>UIA Invoke：与未拖动的左键单击等价（rev-18-prep-uia §3.1）。</summary>
    Uia
}

/// <summary>激活决策纯函数：与 UI 状态解耦，可在无 GUI 的单元测试里跑真值表。</summary>
internal static class ActivationPolicy
{
    /// <summary>
    /// 是否允许激活：窗口隐藏或正在激活时一律拒绝（R6/R7）；
    /// 键盘来源额外要求已进入选中态，鼠标与 UIA 不要求。
    /// </summary>
    internal static bool ShouldActivate(
        ActivationSource source,
        bool selectionActive,
        bool isVisible,
        bool isActivating)
    {
        if (!isVisible || isActivating)
        {
            return false;
        }

        return source != ActivationSource.Keyboard || selectionActive;
    }
}
