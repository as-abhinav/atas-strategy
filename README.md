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

## Key settings and logs

- **Big trade threshold (contracts):** default 300. Applies to aggregated trades (one market-order sweep = one trade), not single prints. Mode defaults to Absolute; "Percentile" and "Both" are available.
- **Min footprint checks:** default 1. Absorption cluster (heavy bid volume) and delta divergence (positive delta) contradict each other on the same bar, so requiring 2 or more rarely passes.
- **Diagnostic log:** on by default. `%APPDATA%\ATAS\Logs\KeyLevelDiagnostics.csv` has one `bar` row per closed bar (levels, active levels, aggregated trades seen, largest trade, threshold) and one `rejected` row per touched level with the reason. Use it to tune the threshold.
- **Signals:** `%APPDATA%\ATAS\Logs\KeyLevelSignals.csv`.

## Strategy 2: NY open range breakout

`Strategies/OpeningRangeBreakoutStrategy.cs` ("NY open range breakout (signal only)" in ATAS). Places no orders.

- Marks the high and low of the first hour after the New York open (09:30-10:30 ET by default) as two lines.
- Signals when a closed bar breaks out of that range: a notification, a marker on the chart, and a line in `%APPDATA%\ATAS\Logs\OpeningRangeSignals.csv`. Breakouts in history are marked on the chart but not notified.
- Each direction signals once per session by default ("First breakout only" stops after the first one). No signals after 16:00 ET.

| Setting | Default | Meaning |
|---|---|---|
| Chart time zone | Local | Time zone of the chart's candle times: Local (this PC), UTC, or NewYork. Check the trace log to confirm. |
| Open hour / minute (NY) | 9 / 30 | Start of the range, New York time. Daylight saving is handled. |
| Range length (minutes) | 60 | Length of the opening range. |
| Stop signals at (NY) | 16:00 | No breakout signals at or after this time. |
| Breakout mode | CloseBeyondRange | CloseBeyondRange needs a bar close outside the range; TouchRange only needs the high/low to trade outside. |
| Breakout buffer (price points) | 0 | Extra distance beyond the range required for a breakout. |
| First breakout only | off | When on, only the first breakout of the session is signalled. |
| Draw range lines / markers | on | Chart visuals. |

Trace log: `%APPDATA%\ATAS\Logs\OpeningRangeTrace.log`. It records the build tag, settings, each session's range with the chart time and the converted NY time, live signals and a heartbeat about once a minute. If the "NY" times in it are not 09:30/10:30 for the open, change "Chart time zone".
