namespace AtasStrategies.Core;

/// <summary>Period keys for grouping bars. Weeks start on Monday.</summary>
public static class PeriodKeys
{
    public static long Day(DateTime time) => time.Date.Ticks;

    public static long Week(DateTime time)
    {
        int offset = ((int)time.DayOfWeek + 6) % 7; // Monday = 0
        return time.Date.AddDays(-offset).Ticks;
    }
}

/// <summary>
/// Builds higher-timeframe bars (daily, weekly) from a lower-timeframe bar stream.
/// Feed bars in chronological order. Completed periods are kept in <see cref="Completed"/>.
/// </summary>
public sealed class PeriodAggregator
{
    private readonly Func<DateTime, long> _keyOf;
    private long? _currentKey;
    private Bar? _current;

    public PeriodAggregator(Func<DateTime, long> keyOf) => _keyOf = keyOf;

    public List<Bar> Completed { get; } = new();

    /// <summary>Adds a bar. Returns true when the bar starts a new period.</summary>
    public bool Add(Bar bar)
    {
        long key = _keyOf(bar.Time);

        if (_current is null || _currentKey != key)
        {
            if (_current is not null) Completed.Add(_current);
            _currentKey = key;
            _current = bar;
            return true;
        }

        _current = _current with
        {
            High = Math.Max(_current.High, bar.High),
            Low = Math.Min(_current.Low, bar.Low),
            Close = bar.Close,
            Volume = _current.Volume + bar.Volume,
        };
        return false;
    }

    public void Reset()
    {
        _currentKey = null;
        _current = null;
        Completed.Clear();
    }
}
