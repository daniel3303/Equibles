using Equibles.Yahoo.Data.Models;

namespace Equibles.Web.ViewModels.Stocks;

public class PriceTabViewModel {
    public string Ticker { get; set; }
    public List<DailyStockPrice> Prices { get; set; } = [];

    // Pre-computed indicator series (same length as Prices, null-padded at start)
    public List<decimal?> Sma20 { get; set; } = [];
    public List<decimal?> Sma50 { get; set; } = [];
    public List<decimal?> Sma200 { get; set; } = [];
    public List<decimal?> Rsi14 { get; set; } = [];
    public List<decimal?> MacdLine { get; set; } = [];
    public List<decimal?> MacdSignal { get; set; } = [];
    public List<decimal?> MacdHistogram { get; set; } = [];
}
