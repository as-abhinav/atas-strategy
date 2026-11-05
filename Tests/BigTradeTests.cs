using AtasStrategies.Core;

namespace AtasStrategies.Tests;

public class BigTradeTests
{
    private static readonly decimal[] Volumes = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10];

    [Fact]
    public void Absolute_threshold_ignores_distribution()
    {
        Assert.Equal(50m, BigTrades.Threshold(Volumes, 50m, 0.9m, BigTradeThresholdMode.Absolute));
    }

    [Fact]
    public void Percentile_threshold_uses_distribution()
    {
        // 90th percentile of 1..10 = 9
        Assert.Equal(9m, BigTrades.Threshold(Volumes, 0m, 0.9m, BigTradeThresholdMode.Percentile));
    }

    [Fact]
    public void Both_takes_the_larger_of_absolute_and_percentile()
    {
        Assert.Equal(9m, BigTrades.Threshold(Volumes, 5m, 0.9m, BigTradeThresholdMode.Both));
        Assert.Equal(20m, BigTrades.Threshold(Volumes, 20m, 0.9m, BigTradeThresholdMode.Both));
    }

    [Fact]
    public void Percentile_on_empty_falls_back_to_absolute()
    {
        Assert.Equal(7m, BigTrades.Threshold(Array.Empty<decimal>(), 7m, 0.9m, BigTradeThresholdMode.Percentile));
    }

    [Fact]
    public void Summarize_splits_by_side_and_filters_zone_and_threshold()
    {
        var trades = new[]
        {
            new Trade(TestData.T0, 5000m, 30, false), // sell in zone, big
            new Trade(TestData.T0, 5000m, 5, false),  // sell in zone, too small
            new Trade(TestData.T0, 5002m, 40, true),  // buy in zone, big
            new Trade(TestData.T0, 5100m, 99, true),  // buy outside zone
        };

        var s = BigTrades.Summarize(trades, 20m, 4995m, 5005m);

        Assert.Equal(1, s.SellCount);
        Assert.Equal(30m, s.SellVolume);
        Assert.Equal(1, s.BuyCount);
        Assert.Equal(40m, s.BuyVolume);
    }

    [Fact]
    public void Bullish_candidate_when_sellers_absorbed_and_no_new_low()
    {
        var s = new BigTradeSummary(0, 10m, 2, 50m);

        Assert.True(BigTrades.IsBullishAbsorptionCandidate(s, priceMadeNewLow: false, reclaimedLevel: false));
    }

    [Fact]
    public void Not_bullish_candidate_when_price_breaks_lower_and_does_not_reclaim()
    {
        var s = new BigTradeSummary(0, 10m, 2, 50m);

        Assert.False(BigTrades.IsBullishAbsorptionCandidate(s, priceMadeNewLow: true, reclaimedLevel: false));
    }

    [Fact]
    public void Bearish_candidate_is_the_mirror()
    {
        var s = new BigTradeSummary(2, 50m, 0, 10m);

        Assert.True(BigTrades.IsBearishAbsorptionCandidate(s, priceMadeNewHigh: true, rejectedLevel: true));
        Assert.False(BigTrades.IsBearishAbsorptionCandidate(s, priceMadeNewHigh: true, rejectedLevel: false));
    }
}
