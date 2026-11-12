namespace AtasStrategies.Core;

public static class TimeZones
{
    private static readonly Lazy<TimeZoneInfo> NewYorkZone = new(ResolveNewYork);

    /// <summary>US Eastern time (handles daylight saving). Looks up the IANA id, then the Windows id.</summary>
    public static TimeZoneInfo NewYork => NewYorkZone.Value;

    /// <summary>
    /// Converts a chart time stamp to New York time. The time stamp's own Kind is ignored:
    /// it is treated as a wall-clock time in <paramref name="chartZone"/>.
    /// </summary>
    public static DateTime ToNewYork(DateTime chartTime, TimeZoneInfo chartZone)
    {
        var unspecified = DateTime.SpecifyKind(chartTime, DateTimeKind.Unspecified);

        // A wall-clock time that does not exist (spring-forward gap) cannot be converted; nudge it forward.
        if (chartZone.IsInvalidTime(unspecified))
            unspecified = unspecified.AddHours(1);

        return TimeZoneInfo.ConvertTime(unspecified, chartZone, NewYork);
    }

    private static TimeZoneInfo ResolveNewYork()
    {
        foreach (string id in new[] { "America/New_York", "Eastern Standard Time" })
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (TimeZoneNotFoundException)
            {
            }
            catch (InvalidTimeZoneException)
            {
            }
        }

        throw new InvalidOperationException("Could not find the New York time zone on this machine.");
    }
}
