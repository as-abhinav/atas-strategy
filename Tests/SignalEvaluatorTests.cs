using AtasStrategies.Core;

namespace AtasStrategies.Tests;

public class SignalEvaluatorTests
{
    private static readonly DivergenceResult Div = new(-10m, -20m, 99m, 99.5m, "Bullish hidden CVD divergence");
    private static readonly AuctionEvidence FullAuction = new(true, true, true, true);

    [Fact]
    public void Signal_when_every_check_passes()
    {
        var d = SignalEvaluator.Decide(Side.Long, true, true, Div, FullAuction, minAuctionEvidence: 1);

        Assert.True(d.IsSignal);
        Assert.Single(d.Reasons);
    }

    [Fact]
    public void Each_failed_check_is_listed()
    {
        var d = SignalEvaluator.Decide(Side.Long, false, false, null, new AuctionEvidence(false, false, false, false), 1);

        Assert.False(d.IsSignal);
        Assert.Equal(4, d.Reasons.Count);
        Assert.Contains(d.Reasons, r => r.Contains("key level"));
        Assert.Contains(d.Reasons, r => r.Contains("absorption"));
        Assert.Contains(d.Reasons, r => r.Contains("CVD divergence"));
        Assert.Contains(d.Reasons, r => r.Contains("Auction not finished"));
    }

    [Fact]
    public void Auction_below_minimum_rejects()
    {
        var d = SignalEvaluator.Decide(Side.Short, true, true, Div, new AuctionEvidence(true, false, false, false), 2);

        Assert.False(d.IsSignal);
        Assert.Contains(d.Reasons, r => r.Contains("1 of 2"));
    }
}
