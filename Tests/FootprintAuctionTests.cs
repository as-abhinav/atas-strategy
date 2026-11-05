using AtasStrategies.Core;

namespace AtasStrategies.Tests;

public class FootprintAuctionTests
{
    [Fact]
    public void Long_support_bar_with_all_checks_passing()
    {
        // Level 100. Bar dips to 99, closes back at 100.5, opened below level, bid-heavy.
        var bar = new FootprintBar(Open: 99.5m, High: 101m, Low: 99m, Close: 100.5m,
            Volume: 100m, BidVolume: 70m, AskVolume: 30m, Delta: 15m);

        var e = FootprintAuction.Evaluate(bar, 100m, Side.Long, absorptionShare: 0.6m);

        Assert.True(e.AbsorptionCluster);
        Assert.True(e.DeltaDivergence);
        Assert.True(e.FailedBreakout);
        Assert.True(e.Reclaim);
        Assert.Equal(4, e.Count);
    }

    [Fact]
    public void Long_bar_without_breakdown_fails_failed_breakout_and_reclaim()
    {
        // Never reached the level below, so no failed breakdown.
        var bar = new FootprintBar(Open: 101m, High: 102m, Low: 100.5m, Close: 101m,
            Volume: 100m, BidVolume: 50m, AskVolume: 50m, Delta: 0m);

        var e = FootprintAuction.Evaluate(bar, 100m, Side.Long, 0.6m);

        Assert.False(e.FailedBreakout);
        Assert.False(e.Reclaim);
        Assert.False(e.AbsorptionCluster);
    }

    [Fact]
    public void Short_resistance_bar_is_the_mirror()
    {
        var bar = new FootprintBar(Open: 100.5m, High: 101m, Low: 99m, Close: 99.5m,
            Volume: 100m, BidVolume: 30m, AskVolume: 70m, Delta: -15m);

        var e = FootprintAuction.Evaluate(bar, 100m, Side.Short, 0.6m);

        Assert.True(e.AbsorptionCluster);
        Assert.True(e.DeltaDivergence);
        Assert.True(e.FailedBreakout);
        Assert.True(e.Reclaim);
    }

    [Fact]
    public void Passes_uses_minimum_count()
    {
        var e = new AuctionEvidence(true, false, true, false);

        Assert.True(e.Passes(2));
        Assert.False(e.Passes(3));
    }
}
