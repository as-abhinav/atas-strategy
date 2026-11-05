using AtasStrategies.Core;

namespace AtasStrategies.Tests;

public class LevelProximityTests
{
    [Fact]
    public void Price_within_proximity_ticks_is_active()
    {
        // ES tick 0.25, 4 ticks = 1.00 point
        Assert.True(LevelProximity.IsActive(5000.75m, 5000.00m, 0.25m, 4));
    }

    [Fact]
    public void Price_beyond_proximity_ticks_is_not_active()
    {
        Assert.False(LevelProximity.IsActive(5001.25m, 5000.00m, 0.25m, 4));
    }

    [Fact]
    public void Zero_ticks_requires_exact_price()
    {
        Assert.True(LevelProximity.IsActive(5000.00m, 5000.00m, 0.25m, 0));
        Assert.False(LevelProximity.IsActive(5000.25m, 5000.00m, 0.25m, 0));
    }

    [Fact]
    public void Non_positive_tick_size_throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => LevelProximity.IsActive(1m, 1m, 0m, 1));
    }
}
