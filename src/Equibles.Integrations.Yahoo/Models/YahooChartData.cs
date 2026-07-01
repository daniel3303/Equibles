namespace Equibles.Integrations.Yahoo.Models;

// The full payload of a single chart fetch: the daily price bars plus any
// split events the same request returned (events=split), so callers get both
// without a second HTTP round-trip.
public class YahooChartData
{
    public List<HistoricalPrice> Prices { get; set; } = [];
    public List<StockSplitEvent> Splits { get; set; } = [];
}
