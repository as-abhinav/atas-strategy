namespace AtasStrategies.Core;

public static class KeyLevelBuilder
{
    /// <summary>
    /// Builds the level set for the current session from completed daily and weekly bars.
    /// Previous day H/L/C and today's open, plus daily and weekly pivot highs/lows within the lookback.
    /// </summary>
    public static IReadOnlyList<KeyLevel> Build(
        IReadOnlyList<Bar> completedDays,
        IReadOnlyList<Bar> completedWeeks,
        decimal currentDayOpen,
        int dailyPivotStrength,
        int dailyLookback,
        int weeklyPivotStrength,
        int weeklyLookback)
    {
        var levels = new List<KeyLevel>();

        if (completedDays.Count == 0)
        {
            levels.Add(new KeyLevel(currentDayOpen, LevelKind.CurrentDayOpen));
        }
        else
        {
            levels.AddRange(KeyLevels.FromPreviousDay(completedDays[^1], currentDayOpen));

            var days = Tail(completedDays, dailyLookback);
            levels.AddRange(SwingPivots.Highs(days, dailyPivotStrength, LevelKind.DailyPivotHigh));
            levels.AddRange(SwingPivots.Lows(days, dailyPivotStrength, LevelKind.DailyPivotLow));
        }

        if (completedWeeks.Count > 0)
        {
            var weeks = Tail(completedWeeks, weeklyLookback);
            levels.AddRange(SwingPivots.Highs(weeks, weeklyPivotStrength, LevelKind.WeeklyPivotHigh));
            levels.AddRange(SwingPivots.Lows(weeks, weeklyPivotStrength, LevelKind.WeeklyPivotLow));
        }

        return levels;
    }

    private static List<Bar> Tail(IReadOnlyList<Bar> bars, int count)
    {
        int skip = Math.Max(0, bars.Count - count);
        var result = new List<Bar>(bars.Count - skip);
        for (int i = skip; i < bars.Count; i++) result.Add(bars[i]);
        return result;
    }
}
