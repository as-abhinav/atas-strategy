using AtasStrategies.Core;

namespace AtasStrategies.Tests;

public class PeriodAggregationTests
{
    private static Bar At(DateTime t, decimal open, decimal high, decimal low, decimal close, decimal volume) =>
        new(t, open, high, low, close, volume);

    [Fact]
    public void Daily_bars_aggregate_intraday_bars()
    {
        var day1 = new DateTime(2026, 1, 5, 9, 30, 0); // Monday
        var agg = new PeriodAggregator(PeriodKeys.Day);

        Assert.True(agg.Add(At(day1, 100, 101, 99, 100.5m, 10)));
        Assert.False(agg.Add(At(day1.AddMinutes(5), 100.5m, 103, 100, 102, 20)));
        Assert.False(agg.Add(At(day1.AddMinutes(10), 102, 102.5m, 98, 99, 30)));

        Assert.Empty(agg.Completed);
    }

    [Fact]
    public void New_day_completes_the_previous_day()
    {
        var day1 = new DateTime(2026, 1, 5, 9, 30, 0);
        var day2 = new DateTime(2026, 1, 6, 9, 30, 0);
        var agg = new PeriodAggregator(PeriodKeys.Day);

        agg.Add(At(day1, 100, 101, 99, 100.5m, 10));
        agg.Add(At(day1.AddMinutes(5), 100.5m, 103, 100, 102, 20));
        Assert.True(agg.Add(At(day2, 102, 104, 101, 103, 5)));

        var completed = Assert.Single(agg.Completed);
        Assert.Equal(103m, completed.High);
        Assert.Equal(99m, completed.Low);
        Assert.Equal(102m, completed.Close);
        Assert.Equal(30m, completed.Volume);
        Assert.Equal(100m, completed.Open);
    }

    [Fact]
    public void Weeks_start_on_monday()
    {
        var monday = new DateTime(2026, 1, 5);
        var sunday = new DateTime(2026, 1, 11);
        var nextMonday = new DateTime(2026, 1, 12);

        Assert.Equal(PeriodKeys.Week(monday), PeriodKeys.Week(sunday));
        Assert.NotEqual(PeriodKeys.Week(sunday), PeriodKeys.Week(nextMonday));
    }

    [Fact]
    public void Reset_clears_state()
    {
        var agg = new PeriodAggregator(PeriodKeys.Day);
        agg.Add(At(new DateTime(2026, 1, 5, 9, 30, 0), 1, 1, 1, 1, 1));
        agg.Add(At(new DateTime(2026, 1, 6, 9, 30, 0), 1, 1, 1, 1, 1));

        agg.Reset();

        Assert.Empty(agg.Completed);
    }
}
