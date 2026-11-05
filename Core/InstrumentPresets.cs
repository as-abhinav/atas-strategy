namespace AtasStrategies.Core;

/// <summary>
/// Starting defaults per instrument. These are not tuned values; adjust them after reviewing
/// signal logs on a live chart.
/// </summary>
public static class InstrumentPresets
{
    public static InstrumentSettings Es { get; } = new("ES", TickSize: 0.25m, ProximityTicks: 6, BigTradeAbsoluteMin: 25m, BigTradePercentile: 0.99m);

    public static InstrumentSettings Nq { get; } = new("NQ", TickSize: 0.25m, ProximityTicks: 12, BigTradeAbsoluteMin: 40m, BigTradePercentile: 0.99m);
}
