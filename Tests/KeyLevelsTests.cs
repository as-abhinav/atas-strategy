using AtasStrategies.Core;

namespace AtasStrategies.Tests;

public class KeyLevelsTests
{
    [Fact]
    public void Previous_day_levels_and_today_open()
    {
        var prevDay = TestData.Bar(0, 5000, 5050, 4980, 5030);

        var levels = KeyLevels.FromPreviousDay(prevDay, 5025m);

        Assert.Equal(4, levels.Count);
        Assert.Contains(levels, l => l.Kind == LevelKind.PreviousDayHigh && l.Price == 5050m);
        Assert.Contains(levels, l => l.Kind == LevelKind.PreviousDayLow && l.Price == 4980m);
        Assert.Contains(levels, l => l.Kind == LevelKind.PreviousDayClose && l.Price == 5030m);
        Assert.Contains(levels, l => l.Kind == LevelKind.CurrentDayOpen && l.Price == 5025m);
    }
}
