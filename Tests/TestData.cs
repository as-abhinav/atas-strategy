using AtasStrategies.Core;

namespace AtasStrategies.Tests;

internal static class TestData
{
    public static readonly DateTime T0 = new(2026, 1, 5, 9, 30, 0);

    public static Bar Bar(int i, decimal open, decimal high, decimal low, decimal close) =>
        new(T0.AddMinutes(5 * i), open, high, low, close, 100);
}
