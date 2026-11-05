namespace AtasStrategies.Core;

public enum BigTradeThresholdMode
{
    Absolute,
    Percentile,
    Both,
}

public sealed record BigTradeSummary(int BuyCount, decimal BuyVolume, int SellCount, decimal SellVolume);

public static class BigTrades
{
    /// <summary>
    /// Volume threshold for a "big" trade. Percentile is 0..1 (e.g. 0.99 = top 1%).
    /// Both = the trade must clear the absolute minimum and the percentile (the larger of the two).
    /// </summary>
    public static decimal Threshold(
        IReadOnlyList<decimal> volumes,
        decimal absoluteMin,
        decimal percentile,
        BigTradeThresholdMode mode)
    {
        if (percentile < 0 || percentile > 1) throw new ArgumentOutOfRangeException(nameof(percentile));

        return mode switch
        {
            BigTradeThresholdMode.Absolute => absoluteMin,
            BigTradeThresholdMode.Percentile => Percentile(volumes, percentile, absoluteMin),
            BigTradeThresholdMode.Both => Math.Max(absoluteMin, Percentile(volumes, percentile, absoluteMin)),
            _ => throw new ArgumentOutOfRangeException(nameof(mode)),
        };
    }

    /// <summary>Summarizes trades inside the price zone that meet the threshold, split by aggressor side.</summary>
    public static BigTradeSummary Summarize(IEnumerable<Trade> trades, decimal threshold, decimal zoneLow, decimal zoneHigh)
    {
        int buyCount = 0, sellCount = 0;
        decimal buyVolume = 0, sellVolume = 0;

        foreach (var t in trades)
        {
            if (t.Volume < threshold || t.Price < zoneLow || t.Price > zoneHigh) continue;

            if (t.IsBuyAggressor)
            {
                buyCount++;
                buyVolume += t.Volume;
            }
            else
            {
                sellCount++;
                sellVolume += t.Volume;
            }
        }

        return new BigTradeSummary(buyCount, buyVolume, sellCount, sellVolume);
    }

    /// <summary>
    /// Bullish absorption candidate: sellers are hitting into a support level (big sell volume
    /// dominates), but price fails to make a new low or quickly reclaims the level.
    /// </summary>
    public static bool IsBullishAbsorptionCandidate(BigTradeSummary s, bool priceMadeNewLow, bool reclaimedLevel) =>
        s.SellVolume > s.BuyVolume && (!priceMadeNewLow || reclaimedLevel);

    /// <summary>Bearish mirror: buyers hitting into resistance, price fails to make a new high or rejects the level.</summary>
    public static bool IsBearishAbsorptionCandidate(BigTradeSummary s, bool priceMadeNewHigh, bool rejectedLevel) =>
        s.BuyVolume > s.SellVolume && (!priceMadeNewHigh || rejectedLevel);

    private static decimal Percentile(IReadOnlyList<decimal> volumes, decimal percentile, decimal fallback)
    {
        if (volumes.Count == 0) return fallback;

        var sorted = volumes.OrderBy(v => v).ToArray();
        int index = (int)Math.Ceiling(percentile * sorted.Length) - 1;
        index = Math.Clamp(index, 0, sorted.Length - 1);
        return sorted[index];
    }
}
