using Xunit;

namespace AssistantTouch.Tests;

/// <summary>「恢复默认」二次确认结果映射：只有确认才执行恢复。</summary>
public class RestoreDefaultsConfirmationTests
{
    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(null, false)]
    public void Apply_OnlyResetsWhenConfirmed(bool? dialogResult, bool expected)
    {
        int resetCount = 0;

        bool applied = RestoreDefaultsConfirmation.Apply(dialogResult, () => resetCount++);

        Assert.Equal(expected, applied);
        Assert.Equal(expected ? 1 : 0, resetCount);
    }

    [Fact]
    public void Apply_Cancel_DoesNotInvokeReset()
    {
        bool called = false;

        Assert.False(RestoreDefaultsConfirmation.Apply(false, () => called = true));
        Assert.False(RestoreDefaultsConfirmation.Apply(null, () => called = true));
        Assert.False(called);
    }
}
