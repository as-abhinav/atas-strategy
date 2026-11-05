using AtasStrategies.Core;

namespace AtasStrategies.Tests;

public class SwingPivotTests
{
    [Fact]
    public void Finds_a_pivot_high_with_strength_one()
    {
        var bars = new[]
        {
            TestData.Bar(0, 1, 10, 0, 5),
            TestData.Bar(1, 5, 20, 4, 15), // pivot high (20) above neighbours
            TestData.Bar(2, 15, 12, 10, 11),
        };

        var highs = SwingPivots.Highs(bars, 1, LevelKind.DailyPivotHigh);

        Assert.Single(highs);
        Assert.Equal(20m, highs[0].Price);
        Assert.Equal(LevelKind.DailyPivotHigh, highs[0].Kind);
    }

    [Fact]
    public void Finds_a_pivot_low_with_strength_one()
    {
        var bars = new[]
        {
            TestData.Bar(0, 10, 12, 8, 9),
            TestData.Bar(1, 9, 10, 4, 6), // pivot low (4)
            TestData.Bar(2, 6, 11, 7, 10),
        };

        var lows = SwingPivots.Lows(bars, 1, LevelKind.WeeklyPivotLow);

        Assert.Single(lows);
        Assert.Equal(4m, lows[0].Price);
    }

    [Fact]
    public void Last_bars_without_confirmation_are_not_pivots()
    {
        var bars = new[]
        {
            TestData.Bar(0, 1, 10, 0, 5),
            TestData.Bar(1, 5, 20, 4, 15), // would be a pivot, but only one bar follows and strength is 2
            TestData.Bar(2, 15, 12, 10, 11),
        };

        Assert.Empty(SwingPivots.Highs(bars, 2, LevelKind.DailyPivotHigh));
    }

    [Fact]
    public void Zero_strength_throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SwingPivots.Highs(Array.Empty<Bar>(), 0, LevelKind.DailyPivotHigh));
    }
}
