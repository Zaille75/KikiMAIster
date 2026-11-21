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
}
