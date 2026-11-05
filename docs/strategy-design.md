# Strategy design: Key-level order-flow reversal (signal-only v1)

Status: draft, open questions answered. Not built yet.

## Confirmed decisions
- Platform: classic ATAS on Windows (`net10.0-windows`).
- Instruments: ES and NQ. Thresholds are set per instrument (separate defaults for each).
- Execution timeframe: 5-minute default, configurable.
- Key levels: daily and weekly pivots only. No H4 levels in v1.
- Mode: signal-only first (draw and log, no orders). Automated trading later.
- Risk settings (per-trade risk, stop, take-profit, daily loss limit): deferred to after v1.

## Data access (from ATAS API docs)
| Need | Source | Notes |
|---|---|---|
| Bars, OHLC | `GetCandle(bar)` | Index 0..CurrentBar-1 |
| Footprint per bar | `IndicatorCandle.Bid/Ask/Volume/Delta/MaxDelta/MinDelta/ValueArea` and `GetAllPriceLevels()` / `GetPriceVolumeInfo(price)` | Per-level fields not yet verified |
| Per-bar delta | `IndicatorCandle.Delta` | Summed in code for CVD |
| Big trades | `OnNewTrade(MarketDataArg)` / `OnCumulativeTrade(CumulativeTrade)` | No built-in big-trade detection; threshold is ours |
| Daily / weekly levels | Daily and weekly bars | To verify: how daily and weekly data are requested from a 5-minute strategy |
| Order placement | `OpenOrderAsync`, `ModifyOrderAsync`, `CancelOrderAsync` | Only in v2 |

Gaps and risks:
- Backtesting is not documented. Verify the signal logic with unit tests on synthetic data, then on a live chart.
- Footprint for past bars is assumed to come from `GetCandle`; verify on a real chart.
- No documented subscription for footprint or CVD. Verify on the account.
- CVD: no built-in cumulative property; accumulate `Delta` per bar.

## Levels
- Previous day high, low, close.
- Current day open.
- Daily pivots and weekly pivots (interpreting "daily and weekly pivots" as the pivot high/low levels from daily and weekly bars; confirm if you meant standard pivot-point formulas).
- Level is active when price is within the proximity threshold (per instrument; 0.05%-0.25% or N ticks) and has not produced a signal in the current session.

## Rule checklist (long; short is the mirror)
1. Price is within the proximity threshold of an active key level.
2. Big trades near the level show sellers absorbed (long) or buyers absorbed (short), with price failing to make a new extreme.
3. Hidden CVD divergence over a configurable lookback (min N candles): CVD makes a new low while price does not.
4. Footprint shows a finished auction: climax, delta divergence, absorption cluster, failed breakdown, or reclaim.
5. Optional: engulfing, rejection wick, or close back through the level.

Entry modes: Aggressive (close of confirmation candle, default), Conservative (retest), Stop-entry. Cooldown per level per session.

## Plan
1. Core pure functions with unit tests: level proximity, big-trade classification, CVD divergence, footprint absorption.
2. Strategy shell: level builder, signal logging, visual markers, `TradingMode = SignalOnly`.
3. Wire the rules into `OnCalculate`; signal logs with full evidence.
4. Verify on a live chart for ES and NQ.
5. Risk management and automated trading later (out of scope for v1).
