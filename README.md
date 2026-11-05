# ATAS Strategies

Chart strategies and indicators for classic ATAS on Windows (.NET 10 class library).

## Layout

- `AtasStrategies.csproj` - project file, references the ATAS platform DLLs.
- `Strategies/` - chart strategies (inherit `ChartStrategy`).
- `Indicators/` - custom chart indicators (inherit `Indicator`).
- `Samples/` - reference samples from the ATAS docs.
- `docs/` - strategy design notes.

## Build

Requires the .NET 10 SDK and classic ATAS installed.

```bash
dotnet build -c Release
```

The platform path defaults to `C:\Program Files (x86)\ATAS Platform`. Override with:

```bash
dotnet build -c Release -p:ATAS_BASE="D:\ATAS Platform"
```

## Install

Copy the built `AtasStrategies.dll` to `%APPDATA%\ATAS\Strategies` (strategies) or `%APPDATA%\ATAS\Indicators` (indicators). Then click the blinking button in the strategies window, or use "Add custom indicator".

## Docs

- Project setup: https://docs.atas.net/en/getting-started/project-setup.html
- Chart strategies: https://docs.atas.net/en/chart-strategies/overview.html

## Core logic and tests

Pure C# rules live in `Core/` (no ATAS references, so they test on any machine). Unit tests are in `Tests/` (xUnit).

```bash
dotnet test Tests/AtasStrategies.Tests.csproj
```

Current coverage: level proximity, swing pivots, previous-day and weekly levels, daily/weekly aggregation, big-trade thresholds and absorption, CVD divergence, footprint auction checks, and the signal checklist (37 tests, all passing).

## Strategy

`Strategies/KeyLevelOrderFlowStrategy.cs` is the ATAS chart strategy (signal only, no orders). It builds daily and weekly levels from the chart's own bars, evaluates each closed bar, draws markers, and appends each signal to `%APPDATA%\ATAS\Logs\KeyLevelSignals.csv`.

Verified: the strategy compiles against stub ATAS types. Not yet verified: the real ATAS API calls (`IndicatorCandle` field types, `MarketDataArg`, `ValueDataSeries`, `RaiseShowNotification`) and behavior on a live chart. Those need the ATAS DLLs on Windows.
