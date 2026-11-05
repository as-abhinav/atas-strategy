namespace AtasStrategies.Core;

public static class KeyLevels
{
    /// <summary>Previous day high, low, close and current day open.</summary>
    public static IReadOnlyList<KeyLevel> FromPreviousDay(Bar previousDay, decimal currentDayOpen) =>
    [
        new KeyLevel(previousDay.High, LevelKind.PreviousDayHigh),
        new KeyLevel(previousDay.Low, LevelKind.PreviousDayLow),
        new KeyLevel(previousDay.Close, LevelKind.PreviousDayClose),
        new KeyLevel(currentDayOpen, LevelKind.CurrentDayOpen),
    ];
}
