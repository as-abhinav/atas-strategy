using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using ATAS.DataFeedsCore;
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
/// New York opening range breakout.
/// Marks the high and low of the first hour after the New York open (09:30 ET by default) and signals
/// when a bar closes outside that range. With "Trading enabled" on and the strategy started, it enters
/// at market on the breakout, places a stop and a take-profit (when one fills the other is cancelled),
/// and flattens before the close.
/// </summary>
[DisplayName("NY open range breakout")]
[Category("AtasStrategies")]
public class OpeningRangeBreakoutStrategy : ChartStrategy
{
    // Bump this when you rebuild, so the trace log proves which DLL ATAS is running.
    private const string BuildTag = "2026-11-12 orb-5-flatten-fix";
    private const string TraceFileName = "OpeningRangeTrace.log";
    private const string SignalFileName = "OpeningRangeSignals.csv";
    private const string SignalHeader = "bar_time_ny,bar_time_chart,direction,price,range_high,range_low,mode";
    private const string TradeFileName = "OpeningRangeTrades.csv";
    private const string TradeHeader = "time,event,direction,quantity,price,stop,target,detail";

    private enum TradeState
    {
        Flat,
        Entering,
        InPosition,
        Exiting,
    }

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

    // Trading state. Order callbacks may arrive on another thread, so all of it is guarded by _sync.
    private readonly object _sync = new();
    private TradeState _state = TradeState.Flat;
    private Order? _entryOrder;
    private Order? _stopOrder;
    private Order? _targetOrder;
    private Order? _exitOrder;
    private RangeBreakout? _activeBreakout;
    private TradePlan? _plan;
    private DateTime? _tradeDay;
    private int _tradesToday;

    // The strategy's own position, signed (+long / -short), updated from its own fills.
    // ATAS's CurrentPosition can lag a fill by seconds, so exit decisions use this instead.
    private decimal _ownPosition;

    // A flatten was requested while the entry order was still working: close it as soon as it fills.
    private bool _flattenRequested;

    // Trace log state.
    private int _calculateCalls;
    private int _olderBarCalls;
    private int _historicalBreakouts;
    private int _liveBreakouts;
    private bool _liveBarTraced;
    private DateTime _lastHeartbeatUtc = DateTime.MinValue;

    // Index of the forming bar when the chart first reached its last bar; -1 while loading history.
    // Every bar that closes from then on is live, even if ATAS (e.g. a fast replay) has already
    // appended newer bars by the time it is calculated.
    private int _firstLiveBar = -1;

    // Set when the settings are invalid; the strategy stays idle (and quiet) until they are fixed.
    private string? _settingsError;

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

    [Display(Name = "Trading enabled", GroupName = "Trading", Order = 10)]
    [OFT.Attributes.Parameter]
    public bool TradingEnabled { get; set; }

    [Display(Name = "Quantity (contracts)", GroupName = "Trading", Order = 20)]
    [Range(1, 1000)]
    [OFT.Attributes.Parameter]
    public decimal Quantity { get; set; } = 1;

    [Display(Name = "Stop placement", GroupName = "Trading", Order = 30)]
    [OFT.Attributes.Parameter]
    public StopPlacement StopMode { get; set; } = StopPlacement.RangeMidpoint;

    [Display(Name = "Target (multiple of risk)", GroupName = "Trading", Order = 40)]
    [Range(0.1, 20)]
    [OFT.Attributes.Parameter]
    public decimal TargetR { get; set; } = 2m;

    [Display(Name = "Max trades per day", GroupName = "Trading", Order = 50)]
    [Range(1, 20)]
    [OFT.Attributes.Parameter]
    public int MaxTradesPerDay { get; set; } = 1;

    [Display(Name = "No new entries after hour (NY)", GroupName = "Trading", Order = 60)]
    [Range(0, 23)]
    [OFT.Attributes.Parameter]
    public int EntryCutoffHour { get; set; } = 15;

    [Display(Name = "No new entries after minute (NY)", GroupName = "Trading", Order = 70)]
    [Range(0, 59)]
    [OFT.Attributes.Parameter]
    public int EntryCutoffMinute { get; set; }

    [Display(Name = "Flatten at hour (NY)", GroupName = "Trading", Order = 80)]
    [Range(0, 23)]
    [OFT.Attributes.Parameter]
    public int FlattenHour { get; set; } = 15;

    [Display(Name = "Flatten at minute (NY)", GroupName = "Trading", Order = 90)]
    [Range(0, 59)]
    [OFT.Attributes.Parameter]
    public int FlattenMinute { get; set; } = 55;

    [Display(Name = "Close position when stopped", GroupName = "Trading", Order = 100)]
    [OFT.Attributes.Parameter]
    public bool ClosePositionOnStop { get; set; } = true;

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

    // ---------------------------------------------------------------- calculation

    protected override void OnCalculate(int bar, decimal value)
    {
        try
        {
            _calculateCalls++;
            if (_calculateCalls <= 3)
                Trace($"OnCalculate call #{_calculateCalls}: bar={bar}, CurrentBar={CurrentBar}.");

            if (bar == 0)
            {
                _settingsError = null;
                StartCalculation();
            }
            else if (_tracker is null && _settingsError is null)
            {
                StartCalculation();
            }

            if (_tracker is null)
                return; // invalid settings: already reported once by StartCalculation

            bool isLiveBar = bar >= CurrentBar - 1;
            if (isLiveBar && _firstLiveBar < 0)
                _firstLiveBar = bar;

            // ATAS can recalculate an older bar after newer ones were processed. Such a call must not drive
            // live decisions (its time is in the past), so it only gets counted and traced.
            if (bar < _nextBarToProcess)
            {
                _olderBarCalls++;
                if (_olderBarCalls <= 5)
                    Trace($"OnCalculate for an older bar: bar={bar}, next bar to process={_nextBarToProcess}, CurrentBar={CurrentBar}. Ignored.");
                return;
            }

            // Process every closed bar, in order. The forming bar (index "bar") is processed once it closes.
            while (_nextBarToProcess < bar)
            {
                bool liveClosedBar = _firstLiveBar >= 0 && _nextBarToProcess >= _firstLiveBar - 1;
                ProcessClosedBar(_nextBarToProcess, liveClosedBar);
                _nextBarToProcess++;
            }

            // Extend the lines onto the forming bar.
            PaintRange(bar);

            if (_firstLiveBar >= 0 && bar >= 1)
            {
                if (!_liveBarTraced)
                {
                    _liveBarTraced = true;
                    TraceLiveStart(bar);
                }

                CheckSessionFlatten();
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

        _firstLiveBar = -1;
        _liveBarTraced = false;

        try
        {
            _tracker = new OpeningRangeTracker(
                sessionStart,
                TimeSpan.FromMinutes(RangeMinutes),
                stop,
                BreakoutBuffer,
                Mode,
                FirstBreakoutOnly);
        }
        catch (ArgumentException ex)
        {
            // Report once and stay idle, instead of failing on every bar.
            _tracker = null;
            _settingsError = ex.Message;
            Trace($"INVALID SETTINGS, strategy idle until fixed: {ex.Message} " +
                  $"(open {sessionStart:hh\\:mm} NY, range {RangeMinutes} min, stop signals {stop:hh\\:mm} NY, buffer {BreakoutBuffer}).");
            RaiseShowNotification($"NY open range strategy: invalid settings, nothing will run. {ex.Message}");
            return;
        }

        _nextBarToProcess = 0;
        _lastHigh = null;
        _lastLow = null;
        _backfilled = false;
        _lastTracedSession = null;
        _historicalBreakouts = 0;

        Trace(
            $"Calculation started. CurrentBar={CurrentBar}. Settings: chart time zone={ChartTimeZone}, " +
            $"open={sessionStart:hh\\:mm} NY, range={RangeMinutes} min, stop signals={stop:hh\\:mm} NY, " +
            $"mode={Mode}, buffer={BreakoutBuffer}, first breakout only={FirstBreakoutOnly}. " +
            $"Trading: enabled={TradingEnabled}, qty={Quantity}, stop={StopMode}, target={TargetR}R, " +
            $"max trades/day={MaxTradesPerDay}, entry cutoff={EntryCutoff:hh\\:mm}, flatten={FlattenTime:hh\\:mm}, " +
            $"close on stop={ClosePositionOnStop}.");
    }

    private void ProcessClosedBar(int index, bool liveClosedBar)
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

        if (!liveClosedBar)
        {
            _historicalBreakouts++;
            return;
        }

        _liveBreakouts++;
        string direction = breakout.Direction == BreakoutDirection.Up ? "UP" : "DOWN";
        string message =
            $"{direction} breakout of NY opening range ({breakout.RangeLow}-{breakout.RangeHigh}) at {breakout.Price}.";
        Trace($"LIVE SIGNAL: {message} Bar NY time {ny:yyyy-MM-dd HH:mm} (bar {index}, newest bar {CurrentBar - 1}).");

        StrategyLog.AppendCsv(SignalFileName, SignalHeader, string.Join(",",
            ny.ToString("yyyy-MM-dd HH:mm"),
            candle.Time.ToString("yyyy-MM-dd HH:mm"),
            breakout.Direction,
            breakout.Price,
            breakout.RangeHigh,
            breakout.RangeLow,
            Mode));

        // One notification per signal that also says what happened to the trade.
        string outcome = TryEnter(breakout);
        RaiseShowNotification($"{message} {outcome}");
    }

    // ---------------------------------------------------------------- trading

    private TimeSpan EntryCutoff => new(EntryCutoffHour, EntryCutoffMinute, 0);

    private TimeSpan FlattenTime => new(FlattenHour, FlattenMinute, 0);

    /// <summary>Tries to enter on a live breakout. Returns a one-line outcome for the notification.</summary>
    private string TryEnter(RangeBreakout breakout)
    {
        if (!TradingEnabled)
            return NoTrade("signal only, 'Trading enabled' is off");

        // CanProcess is true only for the last bar of a started strategy, so ask about the newest bar:
        // the breakout bar itself may no longer be the newest one in a fast replay.
        int newestBar = CurrentBar - 1;
        if (!CanProcess(newestBar))
            return NoTrade("the strategy is not started. Press its start button on the chart before the breakout");

        DateTime nyNow = ToNewYork(GetCandle(newestBar).Time);
        if (nyNow.TimeOfDay >= EntryCutoff)
            return NoTrade($"past the entry cutoff {EntryCutoff:hh\\:mm} NY");

        Order entry;
        lock (_sync)
        {
            if (_tradeDay != nyNow.Date)
            {
                _tradeDay = nyNow.Date;
                _tradesToday = 0;
            }

            if (_state != TradeState.Flat || _ownPosition != 0)
                return NoTrade($"already in a trade (state={_state}, own position={_ownPosition})");

            if (_tradesToday >= MaxTradesPerDay)
                return NoTrade($"max trades per day reached ({_tradesToday})");

            var plan = OrbTradePlanner.Build(
                breakout.Direction, breakout.Price, breakout.RangeHigh, breakout.RangeLow, StopMode, TargetR);
            if (plan is null)
                return NoTrade("entry is already beyond the stop (no valid risk)");

            _activeBreakout = breakout;
            _plan = plan;
            _state = TradeState.Entering;
            _tradesToday++;

            entry = NewOrder(EntryDirection(breakout.Direction), OrderTypes.Market, Quantity);
            _entryOrder = entry;
        }

        Trace($"ENTRY: market {entry.Direction} {Quantity}. Stop placement {StopMode}: planned stop {_plan!.Stop}, " +
              $"target {_plan.Target} ({TargetR}R, risk {_plan.Risk}).");
        LogTrade("entry_sent", entry.Direction, Quantity, breakout.Price, _plan!.Stop, _plan.Target,
            $"market; stop placement {StopMode}; target {TargetR}R; risk {_plan.Risk}");
        _ = SendAsync(entry, "Entry");
        return $"TRADE: market {entry.Direction} {Quantity} sent, stop {_plan.Stop}, target {_plan.Target}.";
    }

    private static string NoTrade(string reason)
    {
        Trace($"No trade: {reason}.");
        return $"No trade: {reason}.";
    }

    protected override void OnOrderChanged(Order order)
    {
        try
        {
            HandleOrderChanged(order);
        }
        catch (Exception ex)
        {
            Trace($"ERROR in OnOrderChanged: {ex}");
            this.LogError("Opening range order handling failed.", ex);
        }
    }

    protected override void OnOrderRegisterFailed(Order order, string message)
    {
        try
        {
            HandleOrderFailure(order, $"register failed: {message}");
        }
        catch (Exception ex)
        {
            Trace($"ERROR in OnOrderRegisterFailed: {ex}");
        }
    }

    protected override void OnStopping()
    {
        try
        {
            Trace($"Strategy stopping. State={_state}, own position={_ownPosition}, platform position={CurrentPosition}, " +
                  $"close on stop={ClosePositionOnStop}.");
            if (ClosePositionOnStop)
                Flatten("strategy stopping");
        }
        catch (Exception ex)
        {
            Trace($"ERROR in OnStopping: {ex}");
        }

        base.OnStopping();
    }

    private void HandleOrderChanged(Order order)
    {
        Order? placeStop = null;
        Order? placeTarget = null;
        Order? cancel = null;
        bool flatten = false;
        string? flattenReason = null;

        lock (_sync)
        {
            if (order == _entryOrder)
            {
                var status = order.Status();

                if (order.State == OrderStates.Failed || status == OrderStatus.Canceled)
                {
                    Trace($"Entry order ended without a fill (state={order.State}, status={status}).");
                    LogTrade("entry_failed", order.Direction, Quantity, 0, 0, 0, $"{order.State}/{status}");
                    ClearTrade();
                    return;
                }

                if (status != OrderStatus.Filled || _state != TradeState.Entering)
                    return; // placed or partly filled: wait for the fill

                _entryOrder = null;
                _state = TradeState.InPosition;
                var b = _activeBreakout!;
                _ownPosition = b.Direction == BreakoutDirection.Up ? Quantity : -Quantity;

                // Recompute the target from the actual fill price; the stop stays on the range.
                decimal fill = AveragePrice > 0 ? AveragePrice : _plan!.Entry;
                var plan = OrbTradePlanner.Build(b.Direction, fill, b.RangeHigh, b.RangeLow, StopMode, TargetR);

                Trace($"Entry filled at {fill}. Own position={_ownPosition} (platform reports {CurrentPosition}).");
                LogTrade("entry_filled", order.Direction, Quantity, fill, plan?.Stop ?? 0, plan?.Target ?? 0,
                    AveragePrice > 0 ? "fill price from platform" : "fill price not reported yet; planned entry used");

                if (_flattenRequested)
                {
                    flatten = true;
                    flattenReason = "flatten was requested before the entry filled";
                }
                else if (plan is null)
                {
                    flatten = true;
                    flattenReason = "fill is beyond the stop";
                }
                else
                {
                    _plan = plan;
                    var exitSide = ExitDirection(b.Direction);
                    placeStop = NewOrder(exitSide, OrderTypes.Stop, Quantity, triggerPrice: ShrinkPrice(plan.Stop));
                    placeTarget = NewOrder(exitSide, OrderTypes.Limit, Quantity, price: ShrinkPrice(plan.Target));
                    _stopOrder = placeStop;
                    _targetOrder = placeTarget;
                    LogTrade("stop_sent", exitSide, Quantity, placeStop.TriggerPrice, plan.Stop, plan.Target, $"stop; placement {StopMode}");
                    LogTrade("target_sent", exitSide, Quantity, placeTarget.Price, plan.Stop, plan.Target, $"limit; {TargetR}R");
                }
            }
            else if (order == _stopOrder || order == _targetOrder)
            {
                bool isStop = order == _stopOrder;
                string which = isStop ? "Stop" : "Target";
                var status = order.Status();

                if (status == OrderStatus.Filled)
                {
                    cancel = isStop ? _targetOrder : _stopOrder;
                    Trace($"{which} filled. Own position now 0. Cancelling the other exit order.");
                    LogTrade(isStop ? "stop_filled" : "target_filled", order.Direction, Quantity,
                        isStop ? order.TriggerPrice : order.Price, _plan?.Stop ?? 0, _plan?.Target ?? 0,
                        isStop ? "order trigger price; actual fill may slip" : "limit price");
                    _ownPosition = 0;
                    ClearTrade();
                }
                else if (order.State == OrderStates.Failed || status == OrderStatus.Canceled)
                {
                    // An exit we did not cancel ourselves is gone: the position is unprotected.
                    flatten = true;
                    flattenReason = $"{which.ToLowerInvariant()} order ended unexpectedly ({order.State}/{status})";
                }
            }
            else if (order == _exitOrder)
            {
                var status = order.Status();
                if (status == OrderStatus.Filled)
                {
                    decimal approxExit = LastPrice();
                    Trace($"Flatten order filled near {approxExit}. Own position now 0.");
                    LogTrade("flatten_filled", order.Direction, order.QuantityToFill, approxExit, _plan?.Stop ?? 0, _plan?.Target ?? 0,
                        "approximate: last traded price when the fill was reported");
                    _ownPosition = 0;
                    ClearTrade();
                }
                else if (order.State == OrderStates.Failed || status == OrderStatus.Canceled)
                {
                    Trace($"FLATTEN ORDER FAILED ({order.State}/{status}). Close the position manually.");
                    RaiseShowNotification("Opening range strategy: flatten order failed. Close the position manually.");
                    _exitOrder = null;
                }
            }
        }

        if (placeStop is not null) _ = SendAsync(placeStop, "Stop");
        if (placeTarget is not null) _ = SendAsync(placeTarget, "Target");
        if (cancel is not null) _ = CancelAsync(cancel);
        if (flatten) Flatten(flattenReason!);
    }

    private void HandleOrderFailure(Order order, string reason)
    {
        bool flatten = false;

        lock (_sync)
        {
            if (order == _entryOrder)
            {
                Trace($"Entry order failed: {reason}.");
                LogTrade("entry_failed", order.Direction, Quantity, 0, 0, 0, reason);
                ClearTrade();
            }
            else if (order == _stopOrder || order == _targetOrder)
            {
                Trace($"Exit order failed: {reason}. Flattening.");
                flatten = true;
            }
            else if (order == _exitOrder)
            {
                Trace($"FLATTEN ORDER FAILED: {reason}. Close the position manually.");
                RaiseShowNotification("Opening range strategy: flatten order failed. Close the position manually.");
                _exitOrder = null;
            }
        }

        if (flatten) Flatten("exit order failed");
    }

    /// <summary>
    /// Closes the trade at the flatten time of its own NY day, or as soon as a later day is reached.
    /// Uses the newest bar on the chart, never the bar being calculated (which can be an older one).
    /// </summary>
    private void CheckSessionFlatten()
    {
        if (_state is not (TradeState.Entering or TradeState.InPosition)) return;
        if (_tradeDay is not DateTime tradeDay) return;

        int newestBar = CurrentBar - 1;
        if (newestBar < 0) return;

        DateTime nyNow = ToNewYork(GetCandle(newestBar).Time);
        bool flattenTimeReached = nyNow.Date == tradeDay && nyNow.TimeOfDay >= FlattenTime;
        bool laterDay = nyNow.Date > tradeDay;

        if (flattenTimeReached || laterDay)
            Flatten($"session flatten time {FlattenTime:hh\\:mm} NY reached: newest bar NY {nyNow:yyyy-MM-dd HH:mm}, trade day {tradeDay:yyyy-MM-dd}");
    }

    /// <summary>Cancels working exit orders and closes any open position at market.</summary>
    private void Flatten(string reason)
    {
        Order? cancelStop;
        Order? cancelTarget;
        Order? cancelEntry;
        Order? exit = null;

        lock (_sync)
        {
            if (_state == TradeState.Exiting) return;

            // Entry still working and already asked to flatten: nothing new to do until it fills or is cancelled.
            if (_flattenRequested && _ownPosition == 0) return;

            cancelStop = _stopOrder;
            cancelTarget = _targetOrder;
            _stopOrder = null;
            _targetOrder = null;

            // Keep the entry order reference: if it fills after being cancelled, the fill handler closes it.
            cancelEntry = _state == TradeState.Entering ? _entryOrder : null;
            if (cancelEntry is not null) _flattenRequested = true;

            decimal position = _ownPosition;
            if (position != 0)
            {
                var side = position > 0 ? OrderDirections.Sell : OrderDirections.Buy;
                exit = NewOrder(side, OrderTypes.Market, Math.Abs(position));
                _exitOrder = exit;
                _state = TradeState.Exiting;
            }
            else if (cancelEntry is null)
            {
                ClearTrade();
            }
        }

        Trace($"FLATTEN ({reason}): own position={_ownPosition}, platform position={CurrentPosition}, cancelling working orders.");
        if (_ownPosition == 0 && CurrentPosition != 0)
            Trace($"WARNING: the platform reports position {CurrentPosition} that this strategy did not open (manual or another " +
                  "strategy). It is not closed by this strategy.");
        bool hadSomething = exit is not null || cancelStop is not null || cancelTarget is not null || cancelEntry is not null;
        if (hadSomething)
            LogTrade("flatten", exit?.Direction ?? OrderDirections.Sell, exit?.QuantityToFill ?? 0, 0, 0, 0, reason);

        if (cancelEntry is not null) _ = CancelAsync(cancelEntry);
        if (cancelStop is not null) _ = CancelAsync(cancelStop);
        if (cancelTarget is not null) _ = CancelAsync(cancelTarget);
        if (exit is not null) _ = SendAsync(exit, "Flatten");
    }

    private void ClearTrade()
    {
        _state = TradeState.Flat;
        _flattenRequested = false;
        _entryOrder = null;
        _stopOrder = null;
        _targetOrder = null;
        _exitOrder = null;
        _activeBreakout = null;
        _plan = null;
    }

    private Order NewOrder(
        OrderDirections direction,
        OrderTypes type,
        decimal quantity,
        decimal price = 0,
        decimal triggerPrice = 0)
    {
        var order = new Order
        {
            Portfolio = Portfolio,
            Security = Security,
            Direction = direction,
            Type = type,
            QuantityToFill = quantity,
        };

        if (type == OrderTypes.Limit) order.Price = price;
        if (type == OrderTypes.Stop) order.TriggerPrice = triggerPrice;
        return order;
    }

    private async Task SendAsync(Order order, string label)
    {
        try
        {
            await OpenOrderAsync(order);

            var status = order.Status();
            Trace($"{label} order sent: {order.Direction} {order.Type} qty={order.QuantityToFill} " +
                  $"price={order.Price} trigger={order.TriggerPrice}. Status={status}, state={order.State}.");

            // None means it was not sent (not started, trading blocked, or volume check failed).
            if (status == OrderStatus.None)
                HandleOrderFailure(order, "not sent (status None)");
        }
        catch (Exception ex)
        {
            HandleOrderFailure(order, $"rejected: {ex.Message}");
        }
    }

    private async Task CancelAsync(Order order)
    {
        try
        {
            if (order.State != OrderStates.Active) return;
            await CancelOrderAsync(order);
            Trace($"Cancelled {order.Type} order.");
        }
        catch (Exception ex)
        {
            Trace($"Cancel of {order.Type} order failed: {ex.Message}");
        }
    }

    /// <summary>Close of the newest bar: an approximation of the current price, for logging exits.</summary>
    private decimal LastPrice()
    {
        try
        {
            return CurrentBar > 0 ? (decimal)GetCandle(CurrentBar - 1).Close : 0m;
        }
        catch (Exception)
        {
            return 0m;
        }
    }

    private static OrderDirections EntryDirection(BreakoutDirection d) =>
        d == BreakoutDirection.Up ? OrderDirections.Buy : OrderDirections.Sell;

    private static OrderDirections ExitDirection(BreakoutDirection d) =>
        d == BreakoutDirection.Up ? OrderDirections.Sell : OrderDirections.Buy;

    private static void LogTrade(
        string evt, OrderDirections direction, decimal quantity, decimal price, decimal stop, decimal target, string detail) =>
        StrategyLog.AppendCsv(TradeFileName, TradeHeader, string.Join(",",
            DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            evt,
            direction,
            quantity,
            price,
            stop,
            target,
            "\"" + detail.Replace("\"", "'") + "\""));

    // ---------------------------------------------------------------- drawing, time, logging

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
            $"complete={_tracker?.IsComplete}. Breakouts found in history: {_historicalBreakouts}. " +
            $"Portfolio: {Portfolio}. Trading enabled: {TradingEnabled}.");

        string mode = TradingEnabled ? $"TRADING ON, qty {Quantity}" : "signals only (Trading enabled is off)";
        RaiseShowNotification(
            $"NY open range strategy running (build {BuildTag}), {mode}. Trace log: %APPDATA%\\ATAS\\Logs\\{TraceFileName}");
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
            $"live signals={_liveBreakouts}, trade state={_state}, own position={_ownPosition}, " +
            $"platform position={CurrentPosition}, trades today={_tradesToday}, older-bar calls={_olderBarCalls}.");
    }

    private static void Trace(string message) => StrategyLog.Trace(TraceFileName, message);

    // Dots if this ATAS version has them; otherwise lines. Resolved by name so it compiles either way.
    private static VisualMode MarkerVisualMode() =>
        Enum.TryParse<VisualMode>("Dots", ignoreCase: true, out var mode) ? mode : VisualMode.Line;
}
