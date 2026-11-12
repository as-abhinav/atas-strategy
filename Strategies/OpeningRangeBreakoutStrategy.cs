using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using ATAS.Indicators;
using ATAS.Strategies.Chart;
using AtasStrategies.Core;
using Utils.Common.Logging;

namespace AtasStrategies.Strategies;

/// <summary>Time zone the chart's candle times are in.</summary>
public enum ChartTimeZoneChoice
{
    Local,
    UTC,
    NewYork,
}

/// <summary>
/// Signal-only New York opening range breakout. Places no orders.
/// Marks the high and low of the first hour after the New York open (09:30 ET by default)
/// and signals when a bar breaks out of that range.
/// </summary>
[DisplayName("NY open range breakout (signal only)")]
[Category("AtasStrategies")]
public class OpeningRangeBreakoutStrategy : ChartStrategy
{
    // Bump this when you rebuild, so the trace log proves which DLL ATAS is running.
    private const string BuildTag = "2026-11-12 orb-1";
    private const string TraceFileName = "OpeningRangeTrace.log";
    private const string SignalFileName = "OpeningRangeSignals.csv";
    private const string SignalHeader = "bar_time_ny,bar_time_chart,direction,price,range_high,range_low,mode";

    private readonly ValueDataSeries _rangeHigh;
    private readonly ValueDataSeries _rangeLow;
    private readonly ValueDataSeries _breakUp;
    private readonly ValueDataSeries _breakDown;

    private OpeningRangeTracker? _tracker;
    private int _nextBarToProcess;
    private decimal? _lastHigh;
    private decimal? _lastLow;
    private bool _backfilled;
    private DateTime? _lastTracedSession;

    // Trace log state.
    private int _calculateCalls;
    private int _historicalBreakouts;
    private int _liveBreakouts;
    private bool _liveBarTraced;
    private DateTime _lastHeartbeatUtc = DateTime.MinValue;

    [Display(Name = "Chart time zone", GroupName = "Session", Order = 10)]
    [OFT.Attributes.Parameter]
    public ChartTimeZoneChoice ChartTimeZone { get; set; } = ChartTimeZoneChoice.Local;

    [Display(Name = "Open hour (NY)", GroupName = "Session", Order = 20)]
    [Range(0, 23)]
    [OFT.Attributes.Parameter]
    public int OpenHour { get; set; } = 9;

    [Display(Name = "Open minute (NY)", GroupName = "Session", Order = 30)]
    [Range(0, 59)]
    [OFT.Attributes.Parameter]
    public int OpenMinute { get; set; } = 30;

    [Display(Name = "Range length (minutes)", GroupName = "Session", Order = 40)]
    [Range(1, 480)]
    [OFT.Attributes.Parameter]
    public int RangeMinutes { get; set; } = 60;

    [Display(Name = "Stop signals at hour (NY)", GroupName = "Session", Order = 50)]
    [Range(0, 23)]
    [OFT.Attributes.Parameter]
    public int StopHour { get; set; } = 16;

    [Display(Name = "Stop signals at minute (NY)", GroupName = "Session", Order = 60)]
    [Range(0, 59)]
    [OFT.Attributes.Parameter]
    public int StopMinute { get; set; }

    [Display(Name = "Breakout mode", GroupName = "Breakout", Order = 10)]
    [OFT.Attributes.Parameter]
    public BreakoutMode Mode { get; set; } = BreakoutMode.CloseBeyondRange;

    [Display(Name = "Breakout buffer (price points)", GroupName = "Breakout", Order = 20)]
    [Range(0, 1000)]
    [OFT.Attributes.Parameter]
    public decimal BreakoutBuffer { get; set; }

    [Display(Name = "First breakout only", GroupName = "Breakout", Order = 30)]
    [OFT.Attributes.Parameter]
    public bool FirstBreakoutOnly { get; set; }

    [Display(Name = "Draw range lines", GroupName = "Visuals", Order = 10)]
    [OFT.Attributes.Parameter]
    public bool DrawRange { get; set; } = true;

    [Display(Name = "Draw breakout markers", GroupName = "Visuals", Order = 20)]
    [OFT.Attributes.Parameter]
    public bool DrawSignals { get; set; } = true;

    public OpeningRangeBreakoutStrategy()
    {
        // Series 0 is the chart's default series; reuse it as the range-high line.
        _rangeHigh = (ValueDataSeries)DataSeries[0];
        _rangeHigh.Name = "Opening range high";
        _rangeHigh.Color = System.Drawing.Color.DodgerBlue.Convert();
        _rangeHigh.VisualType = VisualMode.Line;
        _rangeHigh.Width = 2;

        _rangeLow = new ValueDataSeries("Opening range low")
        {
            Color = System.Drawing.Color.Orange.Convert(),
            VisualType = VisualMode.Line,
            Width = 2,
        };
        DataSeries.Add(_rangeLow);

        var markerMode = MarkerVisualMode();
        _breakUp = new ValueDataSeries("Breakout up")
        {
            Color = System.Drawing.Color.LimeGreen.Convert(),
            VisualType = markerMode,
            Width = 4,
        };
        DataSeries.Add(_breakUp);

        _breakDown = new ValueDataSeries("Breakout down")
        {
            Color = System.Drawing.Color.Red.Convert(),
            VisualType = markerMode,
            Width = 4,
        };
        DataSeries.Add(_breakDown);

        Trace($"Strategy created. Build: {BuildTag}.");
    }

    protected override void OnCalculate(int bar, decimal value)
    {
        try
        {
            _calculateCalls++;
            if (_calculateCalls <= 3)
                Trace($"OnCalculate call #{_calculateCalls}: bar={bar}, CurrentBar={CurrentBar}.");

            if (bar == 0 || _tracker is null)
                StartCalculation();

            // Process every closed bar, in order. The forming bar (index "bar") is processed once it closes.
            bool isLiveBar = bar >= CurrentBar - 1;
            while (_nextBarToProcess < bar)
            {
                bool newestClosedBar = isLiveBar && _nextBarToProcess == bar - 1;
                ProcessClosedBar(_nextBarToProcess, newestClosedBar);
                _nextBarToProcess++;
            }

            // Extend the lines onto the forming bar.
            PaintRange(bar);

            if (isLiveBar && !_liveBarTraced && bar >= 1)
            {
                _liveBarTraced = true;
                TraceLiveStart(bar);
            }

            TraceHeartbeat(bar);
        }
        catch (Exception ex)
        {
            Trace($"ERROR in OnCalculate (bar={bar}): {ex}");
            this.LogError("Opening range strategy failed.", ex);
        }
    }

    private void StartCalculation()
    {
        var sessionStart = new TimeSpan(OpenHour, OpenMinute, 0);
        var stop = new TimeSpan(StopHour, StopMinute, 0);

        _tracker = new OpeningRangeTracker(
            sessionStart,
            TimeSpan.FromMinutes(RangeMinutes),
            stop,
            BreakoutBuffer,
            Mode,
            FirstBreakoutOnly);

        _nextBarToProcess = 0;
        _lastHigh = null;
        _lastLow = null;
        _backfilled = false;
        _lastTracedSession = null;
        _historicalBreakouts = 0;

        Trace(
            $"Calculation started. CurrentBar={CurrentBar}. Settings: chart time zone={ChartTimeZone}, " +
            $"open={sessionStart:hh\\:mm} NY, range={RangeMinutes} min, stop={stop:hh\\:mm} NY, " +
            $"mode={Mode}, buffer={BreakoutBuffer}, first breakout only={FirstBreakoutOnly}.");
    }

    private void ProcessClosedBar(int index, bool newestClosedBar)
    {
        var candle = GetCandle(index);
        DateTime ny = ToNewYork(candle.Time);

        var breakout = _tracker!.Add(ny, (decimal)candle.High, (decimal)candle.Low, (decimal)candle.Close);

        if (_tracker.High is decimal high && _tracker.Low is decimal low)
        {
            _lastHigh = high;
            _lastLow = low;
        }

        PaintRange(index);

        if (_tracker.IsComplete && _tracker.SessionDate != _lastTracedSession)
        {
            _lastTracedSession = _tracker.SessionDate;
            Trace(
                $"Range complete for {_tracker.SessionDate:yyyy-MM-dd}: high={_tracker.High}, low={_tracker.Low}. " +
                $"First bar after the range: chart time {candle.Time:yyyy-MM-dd HH:mm} = NY {ny:yyyy-MM-dd HH:mm}.");
        }

        if (breakout is null) return;

        if (DrawSignals)
        {
            if (breakout.Direction == BreakoutDirection.Up) _breakUp[index] = (decimal)candle.Low;
            else _breakDown[index] = (decimal)candle.High;
        }

        if (!newestClosedBar)
        {
            _historicalBreakouts++;
            return;
        }

        _liveBreakouts++;
        string direction = breakout.Direction == BreakoutDirection.Up ? "UP" : "DOWN";
        string message =
            $"{direction} breakout of NY opening range ({breakout.RangeLow}-{breakout.RangeHigh}) at {breakout.Price}.";
        RaiseShowNotification(message);
        Trace($"LIVE SIGNAL: {message} Bar NY time {ny:yyyy-MM-dd HH:mm}.");

        StrategyLog.AppendCsv(SignalFileName, SignalHeader, string.Join(",",
            ny.ToString("yyyy-MM-dd HH:mm"),
            candle.Time.ToString("yyyy-MM-dd HH:mm"),
            breakout.Direction,
            breakout.Price,
            breakout.RangeHigh,
            breakout.RangeLow,
            Mode));
    }

    /// <summary>
    /// Writes the current range onto a bar. Bars before the first known range are backfilled once,
    /// so the lines never hold zero (which could stretch the price scale).
    /// </summary>
    private void PaintRange(int index)
    {
        if (!DrawRange || _lastHigh is not decimal high || _lastLow is not decimal low) return;

        if (!_backfilled)
        {
            for (int i = 0; i < index; i++)
            {
                _rangeHigh[i] = high;
                _rangeLow[i] = low;
            }
            _backfilled = true;
        }

        _rangeHigh[index] = high;
        _rangeLow[index] = low;
    }

    private DateTime ToNewYork(DateTime chartTime)
    {
        TimeZoneInfo zone = ChartTimeZone switch
        {
            ChartTimeZoneChoice.UTC => TimeZoneInfo.Utc,
            ChartTimeZoneChoice.NewYork => TimeZones.NewYork,
            _ => TimeZoneInfo.Local,
        };
        return TimeZones.ToNewYork(chartTime, zone);
    }

    private void TraceLiveStart(int bar)
    {
        var last = GetCandle(bar - 1);
        Trace(
            $"First live bar reached: bar={bar}, CurrentBar={CurrentBar}. " +
            $"Last closed bar: chart time {last.Time:yyyy-MM-dd HH:mm} = NY {ToNewYork(last.Time):yyyy-MM-dd HH:mm} " +
            $"(chart time zone setting: {ChartTimeZone}, this PC: {TimeZoneInfo.Local.Id}). " +
            $"Current session {_tracker?.SessionDate:yyyy-MM-dd}: high={_tracker?.High}, low={_tracker?.Low}, " +
            $"complete={_tracker?.IsComplete}. Breakouts found in history: {_historicalBreakouts}.");

        RaiseShowNotification(
            $"NY open range strategy running (build {BuildTag}). Trace log: %APPDATA%\\ATAS\\Logs\\{TraceFileName}");
    }

    private void TraceHeartbeat(int bar)
    {
        var now = DateTime.UtcNow;
        if (now - _lastHeartbeatUtc < TimeSpan.FromSeconds(60)) return;
        _lastHeartbeatUtc = now;

        Trace(
            $"Heartbeat: calls={_calculateCalls}, bar={bar}, CurrentBar={CurrentBar}, " +
            $"session={_tracker?.SessionDate:yyyy-MM-dd}, high={_tracker?.High}, low={_tracker?.Low}, " +
            $"complete={_tracker?.IsComplete}, upBroken={_tracker?.UpBroken}, downBroken={_tracker?.DownBroken}, " +
            $"live signals={_liveBreakouts}.");
    }

    private static void Trace(string message) => StrategyLog.Trace(TraceFileName, message);

    // Dots if this ATAS version has them; otherwise lines. Resolved by name so it compiles either way.
    private static VisualMode MarkerVisualMode() =>
        Enum.TryParse<VisualMode>("Dots", ignoreCase: true, out var mode) ? mode : VisualMode.Line;
}
