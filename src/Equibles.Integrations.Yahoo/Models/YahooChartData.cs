namespace Equibles.Integrations.Yahoo.Models;

// The full payload of a single chart fetch: the daily price bars plus any
// split and dividend events the same request returned (events=div|split), so
// callers get all three without a second HTTP round-trip.
public class YahooChartData
{
    public List<HistoricalPrice> Prices { get; set; } = [];
    public List<StockSplitEvent> Splits { get; set; } = [];
    public List<CashDividendEvent> Dividends { get; set; } = [];
}
