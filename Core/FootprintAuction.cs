namespace AtasStrategies.Core;

/// <summary>Which footprint checks passed for a bar at a level.</summary>
public sealed record AuctionEvidence(
    bool AbsorptionCluster,
    bool DeltaDivergence,
    bool FailedBreakout,
    bool Reclaim)
{
    public int Count =>
        (AbsorptionCluster ? 1 : 0) +
        (DeltaDivergence ? 1 : 0) +
        (FailedBreakout ? 1 : 0) +
        (Reclaim ? 1 : 0);

    public bool Passes(int minimum) => Count >= minimum;
}

public static class FootprintAuction
{
    /// <summary>
    /// Checks a bar against a level for a finished auction.
    /// absorptionShare = share of volume on the absorbing side needed for a cluster (e.g. 0.6).
    /// Long (support): bid-side volume absorbs sellers at lows. Short (resistance): ask-side volume at highs.
    /// </summary>
    public static AuctionEvidence Evaluate(FootprintBar bar, decimal levelPrice, Side side, decimal absorptionShare)
    {
        if (absorptionShare <= 0 || absorptionShare > 1) throw new ArgumentOutOfRangeException(nameof(absorptionShare));

        if (side == Side.Long)
        {
            bool absorption = bar.Volume > 0 && bar.BidVolume / bar.Volume >= absorptionShare && bar.Low <= levelPrice;
            bool deltaDivergence = bar.Low <= levelPrice && bar.Delta > 0;
            bool failedBreakout = bar.Low < levelPrice && bar.Close >= levelPrice;
            bool reclaim = bar.Open < levelPrice && bar.Close > levelPrice;
            return new AuctionEvidence(absorption, deltaDivergence, failedBreakout, reclaim);
        }

        bool shortAbsorption = bar.Volume > 0 && bar.AskVolume / bar.Volume >= absorptionShare && bar.High >= levelPrice;
        bool shortDeltaDivergence = bar.High >= levelPrice && bar.Delta < 0;
        bool shortFailedBreakout = bar.High > levelPrice && bar.Close <= levelPrice;
        bool shortReclaim = bar.Open > levelPrice && bar.Close < levelPrice;
        return new AuctionEvidence(shortAbsorption, shortDeltaDivergence, shortFailedBreakout, shortReclaim);
    }
}
