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
