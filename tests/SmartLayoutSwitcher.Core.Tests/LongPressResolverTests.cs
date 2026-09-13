using SmartLayoutSwitcher.Core;

namespace SmartLayoutSwitcher.Core.Tests;

public class LongPressResolverTests
{
    [Theory]
    [InlineData(0, false)]
    [InlineData(599, false)]
    [InlineData(600, true)]
    [InlineData(601, true)]
    [InlineData(3000, true)]
    public void Threshold_IsInclusive_Min(long durationMs, bool expected) =>
        Assert.Equal(expected, new LongPressResolver(600).IsLongPress(durationMs));

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void InvalidThreshold_Rejected(long thresholdMs) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new LongPressResolver(thresholdMs));
}