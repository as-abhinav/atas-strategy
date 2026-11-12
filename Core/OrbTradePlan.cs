namespace AtasStrategies.Core;

public enum StopPlacement
{
    /// <summary>Stop at the middle of the opening range.</summary>
    RangeMidpoint,

    /// <summary>Stop at the other side of the opening range (wider).</summary>
    OppositeSide,
}

public sealed record TradePlan(
    BreakoutDirection Direction,
    decimal Entry,
    decimal Stop,
    decimal Target,
    decimal Risk);

public static class OrbTradePlanner
{
    /// <summary>
    /// Stop from the range, target as a multiple of the risk (entry to stop).
    /// Returns null when the entry is already at or beyond the stop (no valid risk).
    /// </summary>
    public static TradePlan? Build(
        BreakoutDirection direction,
        decimal entry,
        decimal rangeHigh,
        decimal rangeLow,
        StopPlacement placement,
        decimal targetR)
    {
        if (targetR <= 0) throw new ArgumentOutOfRangeException(nameof(targetR), "Target multiple must be positive.");
        if (rangeHigh < rangeLow) throw new ArgumentException("Range high is below range low.");

        bool isLong = direction == BreakoutDirection.Up;

        decimal stop = placement == StopPlacement.RangeMidpoint
            ? (rangeHigh + rangeLow) / 2
            : isLong ? rangeLow : rangeHigh;

        decimal risk = isLong ? entry - stop : stop - entry;
        if (risk <= 0) return null;

        decimal target = isLong ? entry + risk * targetR : entry - risk * targetR;
        return new TradePlan(direction, entry, stop, target, risk);
    }
}
