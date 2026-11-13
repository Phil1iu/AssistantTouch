using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using Xunit;

namespace AssistantTouch.Tests;

/// <summary>激活策略真值表（rev-18-prep-uia §3.1，0 GUI）。</summary>
public class ActivationPolicyTests
{
    [Theory]
    // 0=Keyboard 1=Mouse 2=Uia（枚举是 internal，xunit 的公有测试签名不能直接用）
    [InlineData(0, true, true, false, true)]
    [InlineData(0, false, true, false, false)]
    [InlineData(1, false, true, false, true)]
    [InlineData(1, true, true, false, true)]
    [InlineData(2, false, true, false, true)]
    [InlineData(2, true, true, false, true)]
    [InlineData(2, false, false, false, false)]
    [InlineData(1, true, false, false, false)]
    [InlineData(2, true, true, true, false)]
    [InlineData(0, true, true, true, false)]
    public void ShouldActivate_MatchesTruthTable(
        int sourceValue,
        bool selectionActive,
        bool isVisible,
        bool isActivating,
        bool expected)
    {
        var source = (ActivationSource)sourceValue;
        Assert.Equal(expected, ActivationPolicy.ShouldActivate(source, selectionActive, isVisible, isActivating));
    }
}

/// <summary>
/// D78-C3：<see cref="RadialCircleAutomationPeer"/> 的 provider 行为。
/// 只构造元素与 peer，不建窗体；WPF 元素要求 STA，所以每个测试跑在 STA 线程上。
/// </summary>
public class RadialCircleAutomationPeerTests
{
    private sealed class ActivationRecorder
    {
        public bool Result { get; set; } = true;

        public int Count { get; private set; }

        public int LastIndex { get; private set; } = -1;

        public ActivationSource? LastSource { get; private set; }

        public bool TryActivate(int index, ActivationSource source)
        {
            Count++;
            LastIndex = index;
            LastSource = source;
            return Result;
        }
    }

    private static void RunSta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                error = exception;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        thread.Join();

        if (error is not null)
        {
            ExceptionDispatchInfo.Capture(error).Throw();
        }
    }

    private static T RunSta<T>(Func<T> action)
    {
        T result = default!;
        RunSta(() => result = action());
        return result;
    }

    private static RadialCircle CreateCircle(
        ActivationRecorder recorder,
        int slotIndex = 0,
        RadialSlotKind kind = RadialSlotKind.Shortcut,
        string nameFallback = "桌面",
        bool isVisible = true)
    {
        return new RadialCircle(slotIndex, kind, nameFallback, recorder.TryActivate, () => isVisible);
    }

    private static AutomationPeer CreatePeer(RadialCircle circle)
    {
        AutomationPeer? peer = UIElementAutomationPeer.CreatePeerForElement(circle);
        Assert.NotNull(peer);
        return peer!;
    }

    [Fact]
    public void OnCreateAutomationPeer_ReturnsPeerWithInvokePattern()
    {
        RunSta(() =>
        {
            var recorder = new ActivationRecorder();
            RadialCircle circle = CreateCircle(recorder);

            AutomationPeer peer = CreatePeer(circle);

            Assert.IsType<RadialCircleAutomationPeer>(peer);
            Assert.Same(peer, peer.GetPattern(PatternInterface.Invoke));
        });
    }

    [Fact]
    public void Invoke_CallsTryActivateExactlyOnceWithSlotIndexAndUiaSource()
    {
        RunSta(() =>
        {
            var recorder = new ActivationRecorder();
            RadialCircle circle = CreateCircle(recorder, slotIndex: 2);
            var provider = (IInvokeProvider)CreatePeer(circle).GetPattern(PatternInterface.Invoke)!;

            provider.Invoke();

            Assert.Equal(1, recorder.Count);
            Assert.Equal(2, recorder.LastIndex);
            Assert.Equal(ActivationSource.Uia, recorder.LastSource);
        });
    }

    [Fact]
    public void Invoke_WhenHidden_DoesNotCallTryActivate()
    {
        RunSta(() =>
        {
            var recorder = new ActivationRecorder();
            RadialCircle circle = CreateCircle(recorder, isVisible: false);
            var provider = (IInvokeProvider)CreatePeer(circle).GetPattern(PatternInterface.Invoke)!;

            provider.Invoke();

            Assert.Equal(0, recorder.Count);
        });
    }

    [Fact]
    public void Invoke_WhenTryActivateRejects_DoesNotThrow()
    {
        RunSta(() =>
        {
            var recorder = new ActivationRecorder { Result = false };
            RadialCircle circle = CreateCircle(recorder);
            var provider = (IInvokeProvider)CreatePeer(circle).GetPattern(PatternInterface.Invoke)!;

            provider.Invoke();

            Assert.Equal(1, recorder.Count);
        });
    }

    [Fact]
    public void Name_FallsBackToTooltipWhenAutomationNameIsEmpty()
    {
        RunSta(() =>
        {
            var recorder = new ActivationRecorder();
            RadialCircle circle = CreateCircle(recorder, nameFallback: "任务管理器");
            AutomationPeer peer = CreatePeer(circle);

            Assert.Equal("任务管理器", peer.GetName());

            AutomationProperties.SetName(circle, "自定义名称");
            Assert.Equal("自定义名称", peer.GetName());
        });
    }

    [Fact]
    public void ControlTypeAndClassName_AreButtonLike()
    {
        RunSta(() =>
        {
            RadialCircle circle = CreateCircle(new ActivationRecorder());
            AutomationPeer peer = CreatePeer(circle);

            Assert.Equal(AutomationControlType.Button, peer.GetAutomationControlType());
            Assert.Equal("RadialCircle", peer.GetClassName());
        });
    }

    [Fact]
    public void HelpText_DefaultsPerKindAndPrefersAutomationProperty()
    {
        RunSta(() =>
        {
            RadialCircle shortcut = CreateCircle(new ActivationRecorder());
            RadialCircle plus = CreateCircle(new ActivationRecorder(), kind: RadialSlotKind.Plus, nameFallback: "添加快捷启动");

            Assert.Equal("按 Enter 执行；右键更多操作", CreatePeer(shortcut).GetHelpText());
            Assert.Equal("添加系统快捷或应用程序", CreatePeer(plus).GetHelpText());

            AutomationProperties.SetHelpText(shortcut, "自定义帮助");
            Assert.Equal("自定义帮助", CreatePeer(shortcut).GetHelpText());
        });
    }

    [Fact]
    public void AutomationId_IsExposed()
    {
        RunSta(() =>
        {
            RadialCircle circle = CreateCircle(new ActivationRecorder());
            AutomationProperties.SetAutomationId(circle, "shortcut-3");

            Assert.Equal("shortcut-3", CreatePeer(circle).GetAutomationId());
        });
    }

    [Fact]
    public void IsEnabled_PassesThroughElementState()
    {
        RunSta(() =>
        {
            RadialCircle circle = CreateCircle(new ActivationRecorder());
            AutomationPeer peer = CreatePeer(circle);

            Assert.True(peer.IsEnabled());

            circle.IsEnabled = false;
            Assert.False(peer.IsEnabled());
        });
    }

    [Fact]
    public void IsOffscreen_WhenRingWindowHidden_IsTrue()
    {
        RunSta(() =>
        {
            var recorder = new ActivationRecorder();
            RadialCircle circle = CreateCircle(recorder, isVisible: false);

            Assert.True(CreatePeer(circle).IsOffscreen());
        });
    }
}
