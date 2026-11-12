namespace AtasStrategies.Core;

public enum BreakoutDirection
{
    Up,
    Down,
}

public enum BreakoutMode
{
    /// <summary>A bar must close beyond the range (plus buffer). Fewer false signals; confirmed on bar close.</summary>
    CloseBeyondRange,

    /// <summary>A bar's high/low only needs to trade beyond the range (plus buffer). Earlier, noisier.</summary>
    TouchRange,
}

public sealed record RangeBreakout(
    BreakoutDirection Direction,
    DateTime Time,
    decimal Price,
    decimal RangeHigh,
    decimal RangeLow);

/// <summary>
/// Tracks the opening range of a trading session and reports breakouts.
/// Feed bars in chronological order, with times already converted to the session's time zone.
/// The range is the high/low of all bars starting in [sessionStart, sessionStart + rangeLength).
/// Breakouts are reported from the first bar after the range until sessionEnd.
/// </summary>
public sealed class OpeningRangeTracker
{
    private readonly TimeSpan _sessionStart;
    private readonly TimeSpan _rangeLength;
    private readonly TimeSpan _sessionEnd;
    private readonly decimal _buffer;
    private readonly BreakoutMode _mode;
    private readonly bool _firstBreakoutOnly;

    public OpeningRangeTracker(
        TimeSpan sessionStart,
        TimeSpan rangeLength,
        TimeSpan sessionEnd,
        decimal buffer,
        BreakoutMode mode,
        bool firstBreakoutOnly)
    {
        if (rangeLength <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(rangeLength), "Range length must be positive.");
        if (sessionEnd <= sessionStart + rangeLength)
            throw new ArgumentOutOfRangeException(nameof(sessionEnd), "Session end must be after the range ends.");
        if (buffer < 0)
            throw new ArgumentOutOfRangeException(nameof(buffer), "Buffer cannot be negative.");

        _sessionStart = sessionStart;
        _rangeLength = rangeLength;
        _sessionEnd = sessionEnd;
        _buffer = buffer;
        _mode = mode;
        _firstBreakoutOnly = firstBreakoutOnly;
    }

    public DateTime? SessionDate { get; private set; }

    public decimal? High { get; private set; }

    public decimal? Low { get; private set; }

    /// <summary>True once a bar at or after the end of the range has been seen.</summary>
    public bool IsComplete { get; private set; }

    public bool UpBroken { get; private set; }

    public bool DownBroken { get; private set; }

    /// <summary>Adds a closed bar. Returns a breakout when this bar breaks the range.</summary>
    public RangeBreakout? Add(DateTime newYorkTime, decimal high, decimal low, decimal close)
    {
        DateTime date = newYorkTime.Date;
        TimeSpan timeOfDay = newYorkTime.TimeOfDay;

        if (SessionDate != date) StartSession(date);

        if (timeOfDay < _sessionStart) return null;

        if (timeOfDay < _sessionStart + _rangeLength)
        {
            High = High is null ? high : Math.Max(High.Value, high);
            Low = Low is null ? low : Math.Min(Low.Value, low);
            return null;
        }

        if (High is null || Low is null) return null; // no bars inside the range window (data gap)

        IsComplete = true;

        if (timeOfDay >= _sessionEnd) return null;
        if (_firstBreakoutOnly && (UpBroken || DownBroken)) return null;

        decimal upLevel = High.Value + _buffer;
        decimal downLevel = Low.Value - _buffer;

        bool up;
        bool down;
        if (_mode == BreakoutMode.CloseBeyondRange)
        {
            up = close > upLevel;
            down = close < downLevel;
        }
        else
        {
            up = high > upLevel;
            down = low < downLevel;
        }

        up &= !UpBroken;
        down &= !DownBroken;

        if (up && down)
        {
            // Both sides traded on the same bar: follow the close.
            bool closeInUpperHalf = close >= (High.Value + Low.Value) / 2;
            up = closeInUpperHalf;
            down = !closeInUpperHalf;
        }

        if (up)
        {
            UpBroken = true;
            decimal price = _mode == BreakoutMode.CloseBeyondRange ? close : upLevel;
            return new RangeBreakout(BreakoutDirection.Up, newYorkTime, price, High.Value, Low.Value);
        }

        if (down)
        {
            DownBroken = true;
            decimal price = _mode == BreakoutMode.CloseBeyondRange ? close : downLevel;
            return new RangeBreakout(BreakoutDirection.Down, newYorkTime, price, High.Value, Low.Value);
        }

        return null;
    }

    public void Reset()
    {
        SessionDate = null;
        High = null;
        Low = null;
        IsComplete = false;
        UpBroken = false;
        DownBroken = false;
    }

    private void StartSession(DateTime date)
    {
        SessionDate = date;
        High = null;
        Low = null;
        IsComplete = false;
        UpBroken = false;
        DownBroken = false;
    }
}
