using AtasStrategies.Core;

namespace AtasStrategies.Tests;

public class OrbTradePlanTests
{
    [Fact]
    public void Long_with_midpoint_stop_and_2R_target()
    {
        // Range 5090-5110, midpoint 5100. Entry 5112: risk 12, target 5112 + 24.
        var p = OrbTradePlanner.Build(BreakoutDirection.Up, 5112m, 5110m, 5090m, StopPlacement.RangeMidpoint, 2m);

        Assert.NotNull(p);
        Assert.Equal(5100m, p!.Stop);
        Assert.Equal(12m, p.Risk);
        Assert.Equal(5136m, p.Target);
    }

    [Fact]
    public void Short_with_opposite_side_stop_and_1R_target()
    {
        // Range 5090-5110. Short entry 5088: stop 5110, risk 22, target 5066.
        var p = OrbTradePlanner.Build(BreakoutDirection.Down, 5088m, 5110m, 5090m, StopPlacement.OppositeSide, 1m);

        Assert.NotNull(p);
        Assert.Equal(5110m, p!.Stop);
        Assert.Equal(22m, p.Risk);
        Assert.Equal(5066m, p.Target);
    }

    [Fact]
    public void Entry_beyond_the_stop_gives_no_plan()
    {
        // Long filled below the midpoint stop: no valid risk.
        Assert.Null(OrbTradePlanner.Build(BreakoutDirection.Up, 5099m, 5110m, 5090m, StopPlacement.RangeMidpoint, 2m));
    }

    [Fact]
    public void Non_positive_target_multiple_throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            OrbTradePlanner.Build(BreakoutDirection.Up, 5112m, 5110m, 5090m, StopPlacement.RangeMidpoint, 0m));
    }
}
