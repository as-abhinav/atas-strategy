using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using ATAS.DataFeedsCore;
using ATAS.Indicators;
using ATAS.Strategies.Chart;
using AtasStrategies.Core;
using Utils.Common.Logging;

// Aliases take precedence over ATAS namespace types with the same names.
using Bar = AtasStrategies.Core.Bar;
using Trade = AtasStrategies.Core.Trade;
using Side = AtasStrategies.Core.Side;
using FootprintBar = AtasStrategies.Core.FootprintBar;

namespace AtasStrategies.Strategies;

public enum InstrumentChoice
{
    ES,
    NQ,
}

/// <summary>Only signal-only exists in v1. Paper and live trading come later.</summary>
public enum ExecutionModeChoice
{
    SignalOnly,
}

/// <summary>
/// Signal-only key-level order-flow strategy. Places no orders.
/// Evaluates the last closed bar against daily/weekly levels and writes a log line per signal.
/// </summary>
[DisplayName("Key level order flow (signal only)")]
[Category("AtasStrategies")]
public class KeyLevelOrderFlowStrategy : ChartStrategy
{
    private const int VolumeSampleSize = 5000;
    private const string SignalLogFileName = "KeyLevelSignals.csv";

    private readonly ValueDataSeries _longSignals;
    private readonly ValueDataSeries _shortSignals;

    private readonly PeriodAggregator _daily = new(PeriodKeys.Day);
    private readonly PeriodAggregator _weekly = new(PeriodKeys.Week);
    private readonly HashSet<string> _signaledThisSession = new();
    private readonly List<Trade> _tradeBuffer = new();
    private readonly List<decimal> _volumeSample = new();

    private IReadOnlyList<KeyLevel> _levels = Array.Empty<KeyLevel>();
    private int _nextBarToAggregate;

    [Display(Name = "Instrument", GroupName = "Instrument", Order = 10)]
    [OFT.Attributes.Parameter]
    public InstrumentChoice Contract { get; set; } = InstrumentChoice.ES;

    [Display(Name = "Proximity ticks (0 = preset)", GroupName = "Instrument", Order = 20)]
    [Range(0, 1000)]
    [OFT.Attributes.Parameter]
    public int ProximityTicksOverride { get; set; }

    [Display(Name = "Big trade min volume (0 = preset)", GroupName = "Instrument", Order = 30)]
    [Range(0, 1000000)]
    [OFT.Attributes.Parameter]
    public decimal BigTradeMinOverride { get; set; }

    [Display(Name = "Big trade threshold mode", GroupName = "Big trades", Order = 10)]
    [OFT.Attributes.Parameter]
    public BigTradeThresholdMode BigTradeMode { get; set; } = BigTradeThresholdMode.Both;

    [Display(Name = "Big trade percentile (0-1)", GroupName = "Big trades", Order = 20)]
    [Range(0, 1)]
    [OFT.Attributes.Parameter]
    public decimal BigTradePercentile { get; set; } = 0.99m;

    [Display(Name = "Daily pivot strength", GroupName = "Levels", Order = 10)]
    [Range(1, 20)]
    [OFT.Attributes.Parameter]
    public int DailyPivotStrength { get; set; } = 3;

    [Display(Name = "Daily lookback (days)", GroupName = "Levels", Order = 20)]
    [Range(5, 500)]
    [OFT.Attributes.Parameter]
    public int DailyLookbackDays { get; set; } = 60;

    [Display(Name = "Weekly pivot strength", GroupName = "Levels", Order = 30)]
    [Range(1, 20)]
    [OFT.Attributes.Parameter]
    public int WeeklyPivotStrength { get; set; } = 2;

    [Display(Name = "Weekly lookback (weeks)", GroupName = "Levels", Order = 40)]
    [Range(2, 200)]
    [OFT.Attributes.Parameter]
    public int WeeklyLookbackWeeks { get; set; } = 26;

    [Display(Name = "Divergence lookback (bars)", GroupName = "Order flow", Order = 10)]
    [Range(2, 500)]
    [OFT.Attributes.Parameter]
    public int DivergenceLookbackBars { get; set; } = 30;

    [Display(Name = "Divergence min bars per half", GroupName = "Order flow", Order = 20)]
    [Range(1, 250)]
    [OFT.Attributes.Parameter]
    public int DivergenceMinBars { get; set; } = 5;

    [Display(Name = "Absorbing side volume share", GroupName = "Order flow", Order = 30)]
    [Range(0.01, 1)]
    [OFT.Attributes.Parameter]
    public decimal AbsorptionShare { get; set; } = 0.6m;

    [Display(Name = "Min footprint checks", GroupName = "Order flow", Order = 40)]
    [Range(1, 4)]
    [OFT.Attributes.Parameter]
    public int MinAuctionEvidence { get; set; } = 2;

    [Display(Name = "Draw signals", GroupName = "Visuals", Order = 10)]
    [OFT.Attributes.Parameter]
    public bool DrawSignals { get; set; } = true;

    [Display(Name = "Execution mode", GroupName = "Execution", Order = 10)]
    [OFT.Attributes.Parameter]
    public ExecutionModeChoice ExecutionMode { get; set; } = ExecutionModeChoice.SignalOnly;

    public KeyLevelOrderFlowStrategy()
    {
        // Series 0 is the chart's default series; reuse it as the long-signal markers.
        _longSignals = (ValueDataSeries)DataSeries[0];
        _longSignals.Name = "Long signal";
        _longSignals.Color = System.Drawing.Color.LimeGreen.Convert();
        _longSignals.VisualType = VisualMode.Line;

        _shortSignals = new ValueDataSeries("Short signal")
        {
            VisualType = VisualMode.Line,
            Color = System.Drawing.Color.Red.Convert(),
        };
        DataSeries.Add(_shortSignals);
    }

    protected override void OnCalculate(int bar, decimal value)
    {
        if (bar == 0)
        {
            ResetHistoryState();
        }

        // Aggregate every bar that has closed, in order. Repeated ticks on the live bar are ignored here.
        while (_nextBarToAggregate < bar)
        {
            AggregateClosedBar(_nextBarToAggregate);
            _nextBarToAggregate++;
        }

        // Evaluate the bar that just closed, once per new live bar.
        bool isLiveBar = bar == CurrentBar - 1;
        if (!isLiveBar || bar < 1 || ExecutionMode != ExecutionModeChoice.SignalOnly)
            return;

        try
        {
            EvaluateClosedBar(bar - 1, bar);
        }
        catch (Exception ex)
        {
            this.LogError("Key level evaluation failed.", ex);
        }
    }

    protected override void OnNewTrade(MarketDataArg trade)
    {
        base.OnNewTrade(trade);

        if (trade.DataType != MarketDataType.Trade || trade.Direction == TradeDirection.Between)
            return;

        _tradeBuffer.Add(new Trade(trade.Time, trade.Price, trade.Volume, trade.Direction == TradeDirection.Buy));

        _volumeSample.Add(trade.Volume);
        if (_volumeSample.Count > VolumeSampleSize)
            _volumeSample.RemoveAt(0);
    }

    private void ResetHistoryState()
    {
        _daily.Reset();
        _weekly.Reset();
        _signaledThisSession.Clear();
        _levels = Array.Empty<KeyLevel>();
        _nextBarToAggregate = 0;
    }

    private void AggregateClosedBar(int index)
    {
        var candle = GetCandle(index);
        var closed = ToBar(candle);

        bool newWeek = _weekly.Add(closed);
        bool newDay = _daily.Add(closed);
        if (newDay)
        {
            _levels = KeyLevelBuilder.Build(
                _daily.Completed,
                _weekly.Completed,
                (decimal)candle.Open,
                DailyPivotStrength,
                DailyLookbackDays,
                WeeklyPivotStrength,
                WeeklyLookbackWeeks);
            _signaledThisSession.Clear();
        }
    }

    private void EvaluateClosedBar(int index, int liveBar)
    {
        if (index < 1 || _levels.Count == 0) return;

        var candle = GetCandle(index);
        var previous = GetCandle(index - 1);
        var closed = ToBar(candle);
        var prior = ToBar(previous);
        var settings = ResolveInstrument();

        decimal zoneHalfWidth = settings.ProximityTicks * settings.TickSize;

        // Trades that happened while the evaluated bar was forming.
        var windowStart = candle.Time;
        var windowEnd = GetCandle(liveBar).Time;
        PruneTradesBefore(windowStart);
        var windowTrades = _tradeBuffer.Where(t => t.Time >= windowStart && t.Time < windowEnd).ToList();

        decimal threshold = BigTrades.Threshold(
            _volumeSample, settings.BigTradeAbsoluteMin, settings.BigTradePercentile, BigTradeMode);

        var footprint = ToFootprint(candle);
        var divergenceLong = DetectDivergence(index, bullish: true);
        var divergenceShort = DetectDivergence(index, bullish: false);

        foreach (var level in _levels)
        {
            if (_signaledThisSession.Contains(LevelKey(level))) continue;

            bool touched = closed.Low - zoneHalfWidth <= level.Price && level.Price <= closed.High + zoneHalfWidth;
            bool active = touched || LevelProximity.IsActive(closed.Close, level.Price, settings.TickSize, settings.ProximityTicks);
            if (!active) continue;

            Side side = closed.Close >= level.Price ? Side.Long : Side.Short;
            bool isLong = side == Side.Long;

            var summary = BigTrades.Summarize(
                windowTrades, threshold, level.Price - zoneHalfWidth, level.Price + zoneHalfWidth);

            bool candidate = isLong
                ? BigTrades.IsBullishAbsorptionCandidate(summary, closed.Low < prior.Low, closed.Close > level.Price)
                : BigTrades.IsBearishAbsorptionCandidate(summary, closed.High > prior.High, closed.Close < level.Price);

            var divergence = isLong ? divergenceLong : divergenceShort;
            var auction = FootprintAuction.Evaluate(footprint, level.Price, side, AbsorptionShare);
            var decision = SignalEvaluator.Decide(side, active, candidate, divergence, auction, MinAuctionEvidence);

            if (!decision.IsSignal) continue;

            _signaledThisSession.Add(LevelKey(level));
            PublishSignal(index, level, side, closed, summary, divergence, auction, decision);
        }
    }

    private DivergenceResult? DetectDivergence(int endIndex, bool bullish)
    {
        int lookback = DivergenceLookbackBars;
        int start = endIndex - lookback + 1;
        if (start < 0) return null;

        var bars = new List<Bar>(lookback);
        var deltas = new List<decimal>(lookback);
        for (int k = start; k <= endIndex; k++)
        {
            var candle = GetCandle(k);
            bars.Add(ToBar(candle));
            deltas.Add((decimal)candle.Delta);
        }

        var cvd = CvdDivergence.Cumulative(deltas);
        return CvdDivergence.DetectHidden(bars, cvd, lookback, DivergenceMinBars, bullish);
    }

    private void PublishSignal(
        int index,
        KeyLevel level,
        Side side,
        Bar closed,
        BigTradeSummary summary,
        DivergenceResult? divergence,
        AuctionEvidence auction,
        SignalDecision decision)
    {
        if (DrawSignals)
        {
            if (side == Side.Long) _longSignals[index] = closed.Low;
            else _shortSignals[index] = closed.High;
        }

        string sideText = side == Side.Long ? "LONG" : "SHORT";
        RaiseShowNotification($"{sideText} signal at {level.Kind} {level.Price} ({Contract}).");

        WriteSignalLog(closed, level, side, summary, divergence, auction, decision);
    }

    private void WriteSignalLog(
        Bar closed,
        KeyLevel level,
        Side side,
        BigTradeSummary summary,
        DivergenceResult? divergence,
        AuctionEvidence auction,
        SignalDecision decision)
    {
        try
        {
            string folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ATAS", "Logs");
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, SignalLogFileName);

            if (!File.Exists(path))
            {
                File.WriteAllText(path,
                    "time,contract,side,level_kind,level_price,bar_close,big_buy_count,big_buy_volume," +
                    "big_sell_count,big_sell_volume,divergence,auction_checks,reasons" + Environment.NewLine);
            }

            string reasons = string.Join(" | ", decision.Reasons).Replace("\"", "'");
            string line = string.Join(",",
                closed.Time.ToString("yyyy-MM-dd HH:mm:ss"),
                Contract,
                side,
                level.Kind,
                level.Price,
                closed.Close,
                summary.BuyCount,
                summary.BuyVolume,
                summary.SellCount,
                summary.SellVolume,
                divergence?.Description ?? "none",
                auction.Count,
                "\"" + reasons + "\"");

            File.AppendAllText(path, line + Environment.NewLine);
        }
        catch (Exception ex)
        {
            this.LogError("Could not write signal log.", ex);
        }
    }

    private InstrumentSettings ResolveInstrument()
    {
        var preset = Contract == InstrumentChoice.NQ ? InstrumentPresets.Nq : InstrumentPresets.Es;
        return preset with
        {
            ProximityTicks = ProximityTicksOverride > 0 ? ProximityTicksOverride : preset.ProximityTicks,
            BigTradeAbsoluteMin = BigTradeMinOverride > 0 ? BigTradeMinOverride : preset.BigTradeAbsoluteMin,
            BigTradePercentile = BigTradePercentile,
        };
    }

    private void PruneTradesBefore(DateTime cutoff) => _tradeBuffer.RemoveAll(t => t.Time < cutoff);

    private static string LevelKey(KeyLevel level) => $"{level.Kind}:{level.Price}";

    private static Bar ToBar(IndicatorCandle candle) => new(
        candle.Time,
        (decimal)candle.Open,
        (decimal)candle.High,
        (decimal)candle.Low,
        (decimal)candle.Close,
        (decimal)candle.Volume);

    private static FootprintBar ToFootprint(IndicatorCandle candle) => new(
        (decimal)candle.Open,
        (decimal)candle.High,
        (decimal)candle.Low,
        (decimal)candle.Close,
        (decimal)candle.Volume,
        (decimal)candle.Bid,
        (decimal)candle.Ask,
        (decimal)candle.Delta);
}
