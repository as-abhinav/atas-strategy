namespace AtasStrategies.Core;

public static class LevelProximity
{
    /// <summary>True when price is within <paramref name="proximityTicks"/> ticks of the level (inclusive).</summary>
    public static bool IsActive(decimal price, decimal levelPrice, decimal tickSize, int proximityTicks)
    {
        if (tickSize <= 0) throw new ArgumentOutOfRangeException(nameof(tickSize), "Tick size must be positive.");
        if (proximityTicks < 0) throw new ArgumentOutOfRangeException(nameof(proximityTicks), "Proximity cannot be negative.");

        return Math.Abs(price - levelPrice) <= proximityTicks * tickSize;
    }
}
