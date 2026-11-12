using AtasStrategies.Core;

namespace AtasStrategies.Tests;

public class OpeningRangeTests
{
    private static readonly TimeSpan Start = new(9, 30, 0);
    private static readonly TimeSpan OneHour = TimeSpan.FromHours(1);
    private static readonly TimeSpan End = new(16, 0, 0);
    private static readonly DateTime Monday = new(2026, 1, 5);

    private static OpeningRangeTracker Tracker(
        decimal buffer = 0m,
        BreakoutMode mode = BreakoutMode.CloseBeyondRange,
        bool firstOnly = false) =>
        new(Start, OneHour, End, buffer, mode, firstOnly);

    private static DateTime At(int hour, int minute, DateTime? day = null) =>
        (day ?? Monday).AddHours(hour).AddMinutes(minute);

    /// <summary>Feeds a quiet first hour: highs 5010 at most, lows 4990 at least.</summary>
    private static void FeedFirstHour(OpeningRangeTracker t, DateTime? day = null)
    {
        for (int i = 0; i < 12; i++)
        {
            var time = At(9, 30, day).AddMinutes(5 * i);
            decimal high = i == 4 ? 5010m : 5005m;
            decimal low = i == 7 ? 4990m : 4995m;
            Assert.Null(t.Add(time, high, low, 5000m));
        }
    }

    [Fact]
    public void Range_is_the_high_and_low_of_the_first_hour()
    {
        var t = Tracker();
        FeedFirstHour(t);

        Assert.Equal(5010m, t.High);
        Assert.Equal(4990m, t.Low);
        Assert.False(t.IsComplete);
    }

    [Fact]
    public void Bars_before_the_open_are_not_part_of_the_range()
    {
        var t = Tracker();
        t.Add(At(9, 0), 5100m, 4900m, 5000m); // premarket, ignored
        FeedFirstHour(t);

        Assert.Equal(5010m, t.High);
        Assert.Equal(4990m, t.Low);
    }

    [Fact]
    public void Range_completes_on_the_first_bar_after_the_hour()
    {
        var t = Tracker();
        FeedFirstHour(t);

        Assert.Null(t.Add(At(10, 30), 5005m, 4995m, 5000m));
        Assert.True(t.IsComplete);
    }

    [Fact]
    public void Close_above_the_range_signals_an_up_breakout()
    {
        var t = Tracker();
        FeedFirstHour(t);

        var b = t.Add(At(10, 30), 5015m, 5000m, 5012m);

        Assert.NotNull(b);
        Assert.Equal(BreakoutDirection.Up, b!.Direction);
        Assert.Equal(5012m, b.Price);
        Assert.Equal(5010m, b.RangeHigh);
        Assert.Equal(4990m, b.RangeLow);
    }

    [Fact]
    public void Close_below_the_range_signals_a_down_breakout()
    {
        var t = Tracker();
        FeedFirstHour(t);

        var b = t.Add(At(10, 35), 5000m, 4985m, 4988m);

        Assert.NotNull(b);
        Assert.Equal(BreakoutDirection.Down, b!.Direction);
    }

    [Fact]
    public void Wick_beyond_the_range_with_close_inside_does_not_signal_in_close_mode()
    {
        var t = Tracker(mode: BreakoutMode.CloseBeyondRange);
        FeedFirstHour(t);

        Assert.Null(t.Add(At(10, 30), 5020m, 5000m, 5005m));
    }

    [Fact]
    public void Wick_beyond_the_range_signals_in_touch_mode_at_the_range_level()
    {
        var t = Tracker(mode: BreakoutMode.TouchRange);
        FeedFirstHour(t);

        var b = t.Add(At(10, 30), 5020m, 5000m, 5005m);

        Assert.NotNull(b);
        Assert.Equal(BreakoutDirection.Up, b!.Direction);
        Assert.Equal(5010m, b.Price);
    }

    [Fact]
    public void Buffer_must_be_cleared_before_a_signal()
    {
        var t = Tracker(buffer: 1m);
        FeedFirstHour(t);

        Assert.Null(t.Add(At(10, 30), 5012m, 5000m, 5010.5m)); // beyond 5010 but not beyond 5011
        Assert.NotNull(t.Add(At(10, 35), 5013m, 5000m, 5011.25m));
    }

    [Fact]
    public void Each_direction_signals_once_per_session()
    {
        var t = Tracker();
        FeedFirstHour(t);

        Assert.Equal(BreakoutDirection.Up, t.Add(At(10, 30), 5015m, 5005m, 5012m)!.Direction);
        Assert.Null(t.Add(At(10, 35), 5020m, 5010m, 5018m)); // up again: ignored
        Assert.Equal(BreakoutDirection.Down, t.Add(At(11, 0), 5000m, 4980m, 4985m)!.Direction);
    }

    [Fact]
    public void First_breakout_only_blocks_the_other_side()
    {
        var t = Tracker(firstOnly: true);
        FeedFirstHour(t);

        Assert.NotNull(t.Add(At(10, 30), 5015m, 5005m, 5012m));
        Assert.Null(t.Add(At(11, 0), 5000m, 4980m, 4985m));
    }

    [Fact]
    public void A_new_day_starts_a_new_range()
    {
        var t = Tracker();
        FeedFirstHour(t);
        t.Add(At(10, 30), 5015m, 5005m, 5012m);

        var tuesday = Monday.AddDays(1);
        t.Add(At(9, 30, tuesday), 5100m, 5090m, 5095m);

        Assert.Equal(tuesday, t.SessionDate);
        Assert.Equal(5100m, t.High);
        Assert.Equal(5090m, t.Low);
        Assert.False(t.IsComplete);
        Assert.False(t.UpBroken);
    }

    [Fact]
    public void No_signals_after_the_session_end()
    {
        var t = Tracker();
        FeedFirstHour(t);

        Assert.Null(t.Add(At(16, 0), 5100m, 5000m, 5090m));
    }

    [Fact]
    public void No_signals_when_there_was_no_data_inside_the_range_window()
    {
        var t = Tracker();

        Assert.Null(t.Add(At(11, 0), 5100m, 5000m, 5090m));
        Assert.False(t.IsComplete);
    }

    [Fact]
    public void Invalid_settings_throw()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new OpeningRangeTracker(Start, TimeSpan.Zero, End, 0m, BreakoutMode.CloseBeyondRange, false));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new OpeningRangeTracker(Start, OneHour, new TimeSpan(10, 0, 0), 0m, BreakoutMode.CloseBeyondRange, false));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new OpeningRangeTracker(Start, OneHour, End, -1m, BreakoutMode.CloseBeyondRange, false));
    }
}
