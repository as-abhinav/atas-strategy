using AtasStrategies.Core;

namespace AtasStrategies.Tests;

public class TimeZonesTests
{
    [Fact]
    public void Utc_winter_maps_to_est()
    {
        var ny = TimeZones.ToNewYork(new DateTime(2026, 1, 5, 14, 30, 0), TimeZoneInfo.Utc);

        Assert.Equal(new DateTime(2026, 1, 5, 9, 30, 0), ny);
    }

    [Fact]
    public void Utc_summer_maps_to_edt()
    {
        var ny = TimeZones.ToNewYork(new DateTime(2026, 7, 6, 13, 30, 0), TimeZoneInfo.Utc);

        Assert.Equal(new DateTime(2026, 7, 6, 9, 30, 0), ny);
    }

    [Fact]
    public void New_york_to_new_york_is_unchanged()
    {
        var t = new DateTime(2026, 3, 10, 9, 30, 0);

        Assert.Equal(t, TimeZones.ToNewYork(t, TimeZones.NewYork));
    }

    [Fact]
    public void Kind_of_the_input_is_ignored()
    {
        var local = new DateTime(2026, 1, 5, 14, 30, 0, DateTimeKind.Local);
        var utc = new DateTime(2026, 1, 5, 14, 30, 0, DateTimeKind.Utc);

        Assert.Equal(TimeZones.ToNewYork(utc, TimeZoneInfo.Utc), TimeZones.ToNewYork(local, TimeZoneInfo.Utc));
    }
}
