using AtasStrategies.Core;

namespace AtasStrategies.Tests;

public class KeyLevelBuilderTests
{
    [Fact]
    public void Without_history_only_todays_open_is_a_level()
    {
        var levels = KeyLevelBuilder.Build([], [], 5000m, 3, 60, 2, 26);

        var level = Assert.Single(levels);
        Assert.Equal(LevelKind.CurrentDayOpen, level.Kind);
        Assert.Equal(5000m, level.Price);
    }

    [Fact]
    public void Previous_day_levels_are_included_with_todays_open()
    {
        var days = new List<Bar> { TestData.Bar(0, 5000, 5050, 4980, 5030) };

        var levels = KeyLevelBuilder.Build(days, [], 5025m, 3, 60, 2, 26);

        Assert.Contains(levels, l => l.Kind == LevelKind.PreviousDayHigh && l.Price == 5050m);
        Assert.Contains(levels, l => l.Kind == LevelKind.PreviousDayLow && l.Price == 4980m);
        Assert.Contains(levels, l => l.Kind == LevelKind.PreviousDayClose && l.Price == 5030m);
        Assert.Contains(levels, l => l.Kind == LevelKind.CurrentDayOpen && l.Price == 5025m);
    }

    [Fact]
    public void Daily_pivot_is_included_when_confirmed()
    {
        // Pivot high at index 2 (high 120), strength 1 needs neighbours lower.
        var days = new List<Bar>
        {
            TestData.Bar(0, 100, 105, 95, 100),
            TestData.Bar(1, 100, 110, 95, 100),
            TestData.Bar(2, 100, 120, 95, 100),
            TestData.Bar(3, 100, 110, 95, 100),
            TestData.Bar(4, 100, 105, 95, 100),
        };

        var levels = KeyLevelBuilder.Build(days, [], 100m, 1, 60, 2, 26);

        Assert.Contains(levels, l => l.Kind == LevelKind.DailyPivotHigh && l.Price == 120m);
    }

    [Fact]
    public void Lookback_limits_which_days_are_used()
    {
        // Pivot high (120) sits outside a 2-day lookback, so it is ignored.
        var days = new List<Bar>
        {
            TestData.Bar(0, 100, 105, 95, 100),
            TestData.Bar(1, 100, 110, 95, 100),
            TestData.Bar(2, 100, 120, 95, 100),
            TestData.Bar(3, 100, 110, 95, 100),
            TestData.Bar(4, 100, 105, 95, 100),
        };

        var levels = KeyLevelBuilder.Build(days, [], 100m, 1, 2, 2, 26);

        Assert.DoesNotContain(levels, l => l.Kind == LevelKind.DailyPivotHigh);
    }
}
