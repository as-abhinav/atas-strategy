namespace AtasStrategies.Core;

public sealed record DivergenceResult(
    decimal PriorCvdExtreme,
    decimal RecentCvdExtreme,
    decimal PriorPriceExtreme,
    decimal RecentPriceExtreme,
    string Description);

public static class CvdDivergence
{
    /// <summary>Running sum of per-bar delta.</summary>
    public static IReadOnlyList<decimal> Cumulative(IReadOnlyList<decimal> barDeltas)
    {
        var result = new decimal[barDeltas.Count];
        decimal running = 0;
        for (int i = 0; i < barDeltas.Count; i++)
        {
            running += barDeltas[i];
            result[i] = running;
        }
        return result;
    }

    /// <summary>
    /// Hidden (absorption) CVD divergence over the last <paramref name="lookback"/> bars, split into
    /// a prior half and a recent half (each at least <paramref name="minCandles"/> long).
    /// Bullish: CVD makes a lower low while price does not break its prior low.
    /// Bearish: CVD makes a higher high while price does not break its prior high.
    /// Returns null when there is no divergence or not enough bars.
    /// </summary>
    public static DivergenceResult? DetectHidden(
        IReadOnlyList<Bar> bars,
        IReadOnlyList<decimal> cvd,
        int lookback,
        int minCandles,
        bool bullish)
    {
        if (bars.Count != cvd.Count) throw new ArgumentException("Bars and CVD must have the same length.");
        if (minCandles < 1) throw new ArgumentOutOfRangeException(nameof(minCandles));
        if (lookback < 2 * minCandles) return null;
        if (bars.Count < lookback) return null;

        int start = bars.Count - lookback;
        int half = lookback / 2;
        int priorEnd = start + half;

        decimal priorCvd = bullish ? Min(cvd, start, priorEnd) : Max(cvd, start, priorEnd);
        decimal recentCvd = bullish ? Min(cvd, priorEnd, bars.Count) : Max(cvd, priorEnd, bars.Count);

        decimal priorPrice = bullish ? MinLow(bars, start, priorEnd) : MaxHigh(bars, start, priorEnd);
        decimal recentPrice = bullish ? MinLow(bars, priorEnd, bars.Count) : MaxHigh(bars, priorEnd, bars.Count);

        bool cvdDiverges = bullish ? recentCvd < priorCvd : recentCvd > priorCvd;
        bool priceHolds = bullish ? recentPrice >= priorPrice : recentPrice <= priorPrice;

        if (!cvdDiverges || !priceHolds) return null;

        string label = bullish ? "Bullish hidden CVD divergence" : "Bearish hidden CVD divergence";
        return new DivergenceResult(priorCvd, recentCvd, priorPrice, recentPrice, label);
    }

    private static decimal Min(IReadOnlyList<decimal> values, int from, int to)
    {
        decimal m = values[from];
        for (int i = from + 1; i < to; i++) m = Math.Min(m, values[i]);
        return m;
    }

    private static decimal Max(IReadOnlyList<decimal> values, int from, int to)
    {
        decimal m = values[from];
        for (int i = from + 1; i < to; i++) m = Math.Max(m, values[i]);
        return m;
    }

    private static decimal MinLow(IReadOnlyList<Bar> bars, int from, int to)
    {
        decimal m = bars[from].Low;
        for (int i = from + 1; i < to; i++) m = Math.Min(m, bars[i].Low);
        return m;
    }

    private static decimal MaxHigh(IReadOnlyList<Bar> bars, int from, int to)
    {
        decimal m = bars[from].High;
        for (int i = from + 1; i < to; i++) m = Math.Max(m, bars[i].High);
        return m;
    }
}
