namespace AtasStrategies.Core;

/// <summary>Long = support level (buy setup); Short = resistance level (sell setup).</summary>
public enum Side
{
    Long,
    Short,
}

public enum LevelKind
{
    PreviousDayHigh,
    PreviousDayLow,
    PreviousDayClose,
    CurrentDayOpen,
    DailyPivotHigh,
    DailyPivotLow,
    WeeklyPivotHigh,
    WeeklyPivotLow,
}

public sealed record KeyLevel(decimal Price, LevelKind Kind);

/// <summary>OHLCV bar, used for daily and weekly pivot detection.</summary>
public sealed record Bar(DateTime Time, decimal Open, decimal High, decimal Low, decimal Close, decimal Volume);

/// <summary>Footprint summary of one bar on the execution timeframe.</summary>
public sealed record FootprintBar(
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    decimal Volume,
    decimal BidVolume,
    decimal AskVolume,
    decimal Delta);

/// <summary>A single trade. IsBuyAggressor = bought at the offer (market buy).</summary>
public sealed record Trade(DateTime Time, decimal Price, decimal Volume, bool IsBuyAggressor);

/// <summary>Per-instrument settings. ES and NQ each get their own instance.</summary>
public sealed record InstrumentSettings(
    string Symbol,
    decimal TickSize,
    int ProximityTicks,
    decimal BigTradeAbsoluteMin,
    decimal BigTradePercentile);
