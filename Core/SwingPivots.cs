namespace AtasStrategies.Core;

/// <summary>
/// Pivot highs/lows: a bar whose high (low) is strictly greater (lower) than the
/// <paramref name="strength"/> bars on each side. The last <c>strength</c> bars cannot be
/// confirmed yet, so they are never returned.
/// </summary>
public static class SwingPivots
{
    public static IReadOnlyList<KeyLevel> Highs(IReadOnlyList<Bar> bars, int strength, LevelKind kind)
    {
        ValidateStrength(strength);
        var result = new List<KeyLevel>();
        for (int i = strength; i < bars.Count - strength; i++)
        {
            bool isPivot = true;
            for (int k = 1; k <= strength && isPivot; k++)
            {
                if (bars[i].High <= bars[i - k].High || bars[i].High <= bars[i + k].High)
                    isPivot = false;
            }
            if (isPivot) result.Add(new KeyLevel(bars[i].High, kind));
        }
        return result;
    }

    public static IReadOnlyList<KeyLevel> Lows(IReadOnlyList<Bar> bars, int strength, LevelKind kind)
    {
        ValidateStrength(strength);
        var result = new List<KeyLevel>();
        for (int i = strength; i < bars.Count - strength; i++)
        {
            bool isPivot = true;
            for (int k = 1; k <= strength && isPivot; k++)
            {
                if (bars[i].Low >= bars[i - k].Low || bars[i].Low >= bars[i + k].Low)
                    isPivot = false;
            }
            if (isPivot) result.Add(new KeyLevel(bars[i].Low, kind));
        }
        return result;
    }

    private static void ValidateStrength(int strength)
    {
        if (strength < 1) throw new ArgumentOutOfRangeException(nameof(strength), "Pivot strength must be at least 1.");
    }
}
