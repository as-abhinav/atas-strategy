using AtasStrategies.Core;

namespace AtasStrategies.Tests;

public class CvdDivergenceTests
{
    [Fact]
    public void Cumulative_is_a_running_sum()
    {
        var cvd = CvdDivergence.Cumulative([5m, -3m, -4m]);

        Assert.Equal(new[] { 5m, 2m, -2m }, cvd);
    }

    [Fact]
    public void Bullish_hidden_divergence_when_cvd_makes_lower_low_and_price_holds()
    {
        // Lookback 6, halves of 3. Prior half: lows 99,100,100; CVD min -10.
        // Recent half: lows 99.5,100,100.5 (holds above 99); CVD min -20 (new low).
        var bars = new[]
        {
            TestData.Bar(0, 100, 101, 99, 100),
            TestData.Bar(1, 100, 101, 100, 100),
            TestData.Bar(2, 100, 101, 100, 100),
            TestData.Bar(3, 100, 101, 99.5m, 100),
            TestData.Bar(4, 100, 101, 100, 100),
            TestData.Bar(5, 100, 101, 100.5m, 101),
        };
        var cvd = new[] { -10m, -5m, -8m, -20m, -15m, -12m };

        var result = CvdDivergence.DetectHidden(bars, cvd, lookback: 6, minCandles: 3, bullish: true);

        Assert.NotNull(result);
        Assert.Equal("Bullish hidden CVD divergence", result!.Description);
        Assert.Equal(-10m, result.PriorCvdExtreme);
        Assert.Equal(-20m, result.RecentCvdExtreme);
    }

    [Fact]
    public void No_bullish_divergence_when_price_also_makes_lower_low()
    {
        var bars = new[]
        {
            TestData.Bar(0, 100, 101, 99, 100),
            TestData.Bar(1, 100, 101, 100, 100),
            TestData.Bar(2, 100, 101, 100, 100),
            TestData.Bar(3, 100, 101, 98, 100), // breaks prior low
            TestData.Bar(4, 100, 101, 100, 100),
            TestData.Bar(5, 100, 101, 100, 101),
        };
        var cvd = new[] { -10m, -5m, -8m, -20m, -15m, -12m };

        Assert.Null(CvdDivergence.DetectHidden(bars, cvd, 6, 3, bullish: true));
    }

    [Fact]
    public void Not_enough_bars_returns_null()
    {
        var bars = new[] { TestData.Bar(0, 1, 1, 1, 1), TestData.Bar(1, 1, 1, 1, 1) };
        var cvd = new[] { 0m, 0m };

        Assert.Null(CvdDivergence.DetectHidden(bars, cvd, lookback: 6, minCandles: 3, bullish: true));
    }

    [Fact]
    public void Bearish_divergence_is_the_mirror()
    {
        var bars = new[]
        {
            TestData.Bar(0, 100, 101, 99, 100),
            TestData.Bar(1, 100, 100, 99, 100),
            TestData.Bar(2, 100, 100, 99, 100),
            TestData.Bar(3, 100, 100.5m, 99, 100),
            TestData.Bar(4, 100, 100, 99, 100),
            TestData.Bar(5, 100, 100.5m, 99, 100),
        };
        var cvd = new[] { 10m, 5m, 8m, 20m, 15m, 12m };

        var result = CvdDivergence.DetectHidden(bars, cvd, 6, 3, bullish: false);

        Assert.NotNull(result);
        Assert.Equal("Bearish hidden CVD divergence", result!.Description);
    }
}
