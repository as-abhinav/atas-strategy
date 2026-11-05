namespace AtasStrategies.Core;

/// <summary>Result of the rule checklist. Reasons list every failed check, in checklist order.</summary>
public sealed record SignalDecision(bool IsSignal, Side Side, IReadOnlyList<string> Reasons);

public static class SignalEvaluator
{
    /// <summary>
    /// Core checklist (signal-only v1):
    /// 1. Level active. 2. Absorption candidate. 3. Hidden CVD divergence. 4. Finished auction.
    /// Each failed check adds a reason. Signal only when every check passes.
    /// </summary>
    public static SignalDecision Decide(
        Side side,
        bool levelActive,
        bool absorptionCandidate,
        DivergenceResult? divergence,
        AuctionEvidence auction,
        int minAuctionEvidence)
    {
        var reasons = new List<string>();

        if (!levelActive) reasons.Add("Price not at an active key level.");
        if (!absorptionCandidate) reasons.Add("No big-trade absorption at the level.");
        if (divergence is null) reasons.Add("No hidden CVD divergence.");
        if (!auction.Passes(minAuctionEvidence))
            reasons.Add($"Auction not finished: {auction.Count} of {minAuctionEvidence} required footprint checks passed.");

        bool isSignal = reasons.Count == 0;
        if (isSignal)
        {
            string label = side == Side.Long ? "Long" : "Short";
            reasons.Add($"{label} setup: key level, absorption, {divergence!.Description.ToLowerInvariant()}, finished auction.");
        }

        return new SignalDecision(isSignal, side, reasons);
    }
}
