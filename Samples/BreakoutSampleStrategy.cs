using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;

using ATAS.DataFeedsCore;
using ATAS.Strategies.Chart;
using Utils.Common.Logging;

namespace AtasStrategies.Samples;

/// <summary>
/// Sample from the ATAS docs: one market buy when a bar closes above the previous bar's high.
/// Trades for real once started on a live account.
/// </summary>
[DisplayName("Breakout sample")]
public class BreakoutSampleStrategy : ChartStrategy
{
    private Order? _order;

    [Display(Name = "Order volume", GroupName = "Settings", Order = 10)]
    [Range(1, 1000)]
    public decimal OrderVolume { get; set; } = 1;

    protected override void OnCalculate(int bar, decimal value)
    {
        // CanProcess is true only for the last bar of a started strategy.
        if (!CanProcess(bar) || bar < 1 || _order != null)
            return;

        var candle = GetCandle(bar);
        var previous = GetCandle(bar - 1);

        if (candle.Close <= previous.High)
            return;

        _order = new Order
        {
            Portfolio = Portfolio,
            Security = Security,
            Direction = OrderDirections.Buy,
            Type = OrderTypes.Market,
            QuantityToFill = OrderVolume,
        };

        _ = PlaceAsync(_order);
    }

    protected override void OnOrderChanged(Order order)
    {
        if (order != _order)
            return;

        if (order.State == OrderStates.Failed || order.Status() == OrderStatus.Canceled)
            _order = null; // allow a new attempt
    }

    private async Task PlaceAsync(Order order)
    {
        try
        {
            await OpenOrderAsync(order);
        }
        catch (Exception e)
        {
            this.LogError("Order placement failed.", e);
            _order = null;
        }
    }
}
