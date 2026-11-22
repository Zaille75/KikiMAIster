using KikiMAIster.Core;
using Xunit;

namespace KikiMAIster.Core.Tests;

/// 1.2.5 (Zaille75): Godot counts the wait for the last player's end of turn, minus 5 seconds.
public class Update1_2_5Tests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(3, 0)]
    [InlineData(5, 0)]
    [InlineData(5.5, 0.5)]
    [InlineData(32, 27)]
    public void EndTurnWait_DropsTheFirstFiveSeconds(double gap, double counted) =>
        Assert.Equal(counted, WaitRules.EndTurnWait(gap), 3);

    [Fact]
    public void MapWait_NoDrawing_IsTheWholeGap() =>
        Assert.Equal(20, WaitRules.MapWait(10, 30, Array.Empty<(double, double)>()), 3);

    [Fact]
    public void MapWait_RemovesStrokesInsideTheGap() =>
        Assert.Equal(12, WaitRules.MapWait(10, 30, new (double, double)[] { (12, 15), (20, 25) }), 3);

    [Fact]
    public void MapWait_ClipsStrokesToTheGap() =>
        Assert.Equal(14, WaitRules.MapWait(10, 30, new (double, double)[] { (5, 12), (26, 40) }), 3);

    [Fact]
    public void MapWait_IgnoresStrokesOutsideTheGap() =>
        Assert.Equal(20, WaitRules.MapWait(10, 30, new (double, double)[] { (1, 5), (31, 35) }), 3);

    [Fact]
    public void MapWait_CountsOverlapsOnce() =>
        Assert.Equal(14, WaitRules.MapWait(10, 30, new (double, double)[] { (12, 16), (14, 18) }), 3);

    [Fact]
    public void MapWait_DrawingTheWholeGap_IsZero() =>
        Assert.Equal(0, WaitRules.MapWait(10, 30, new (double, double)[] { (9, 31) }), 3);
}
