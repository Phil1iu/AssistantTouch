using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using Xunit;

namespace AssistantTouch.Tests;

/// <summary>
/// D78-C4：悬浮球 peer。只构造元素与 peer，不建窗体；WPF 元素要求 STA，测试跑在 STA 线程。
/// 行为测试直接构造 peer 并注入可见性（游离元素的 IsVisible 恒为 false）。
/// </summary>
public class BallSurfaceAutomationPeerTests
{
    private sealed class InvokeRecorder
    {
        public int Count { get; private set; }

        public bool Invoke()
        {
            Count++;
            return true;
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

    [Fact]
    public void OnCreateAutomationPeer_ReturnsPeerWithInvokePattern()
    {
        RunSta(() =>
        {
            var ball = new BallSurfaceElement { TryInvoke = () => true };

            AutomationPeer? peer = UIElementAutomationPeer.CreatePeerForElement(ball);

            Assert.NotNull(peer);
            Assert.IsType<BallSurfaceAutomationPeer>(peer);
            Assert.Same(peer, peer!.GetPattern(PatternInterface.Invoke));
        });
    }

    [Fact]
    public void Invoke_CallsTryInvokeOnce()
    {
        RunSta(() =>
        {
            var recorder = new InvokeRecorder();
            var ball = new BallSurfaceElement { TryInvoke = recorder.Invoke };
            var peer = new BallSurfaceAutomationPeer(ball, recorder.Invoke, () => true);
            var provider = (IInvokeProvider)peer.GetPattern(PatternInterface.Invoke)!;

            provider.Invoke();

            Assert.Equal(1, recorder.Count);
        });
    }

    [Fact]
    public void Invoke_WhenHidden_DoesNotCall()
    {
        RunSta(() =>
        {
            var recorder = new InvokeRecorder();
            var ball = new BallSurfaceElement { TryInvoke = recorder.Invoke };
            var peer = new BallSurfaceAutomationPeer(ball, recorder.Invoke, () => false);
            var provider = (IInvokeProvider)peer.GetPattern(PatternInterface.Invoke)!;

            provider.Invoke();

            Assert.Equal(0, recorder.Count);
        });
    }

    [Fact]
    public void Invoke_WithoutWiredCallback_DoesNotThrow()
    {
        RunSta(() =>
        {
            var ball = new BallSurfaceElement();
            var peer = new BallSurfaceAutomationPeer(ball, () => ball.TryInvoke?.Invoke() ?? false, () => true);
            var provider = (IInvokeProvider)peer.GetPattern(PatternInterface.Invoke)!;

            provider.Invoke();
        });
    }

    [Fact]
    public void NameAndHelpText_AreExposed()
    {
        RunSta(() =>
        {
            var ball = new BallSurfaceElement { TryInvoke = () => true };
            AutomationProperties.SetName(ball, "助手触摸");
            AutomationProperties.SetHelpText(ball, "左键单击打开快捷菜单；拖动可移动；右键更多操作");
            var peer = new BallSurfaceAutomationPeer(ball, () => true, () => true);

            Assert.Equal("助手触摸", peer.GetName());
            Assert.Equal("左键单击打开快捷菜单；拖动可移动；右键更多操作", peer.GetHelpText());
        });
    }

    [Fact]
    public void HelpText_FallsBackWhenNotSet()
    {
        RunSta(() =>
        {
            var ball = new BallSurfaceElement { TryInvoke = () => true };
            var peer = new BallSurfaceAutomationPeer(ball, () => true, () => true);

            Assert.Equal("左键单击打开快捷菜单；拖动可移动；右键更多操作", peer.GetHelpText());
        });
    }

    [Fact]
    public void ControlTypeAndClassName_AreButtonLike()
    {
        RunSta(() =>
        {
            var ball = new BallSurfaceElement { TryInvoke = () => true };
            var peer = new BallSurfaceAutomationPeer(ball, () => true, () => true);

            Assert.Equal(AutomationControlType.Button, peer.GetAutomationControlType());
            Assert.Equal("BallSurface", peer.GetClassName());
        });
    }

    [Fact]
    public void IsOffscreen_WhenWindowHidden_IsTrue()
    {
        RunSta(() =>
        {
            var ball = new BallSurfaceElement { TryInvoke = () => true };
            var peer = new BallSurfaceAutomationPeer(ball, () => true, () => false);

            Assert.True(peer.IsOffscreen());
        });
    }
}
