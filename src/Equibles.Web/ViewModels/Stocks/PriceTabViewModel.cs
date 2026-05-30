using Equibles.Yahoo.Data.Models;
using Equibles.Yahoo.Repositories;

namespace Equibles.Web.ViewModels.Stocks;

public class PriceTabViewModel : StockTabViewModel
{
    public List<DailyStockPrice> Prices { get; set; } = [];

    // Trailing- and calendar-window returns for this stock.
    public PriceReturns Returns { get; set; } = new();

    // Same windows for the benchmark (SPY); null when the benchmark is
    // unavailable or this stock IS the benchmark.
    public PriceReturns BenchmarkReturns { get; set; }

    // Ticker of the benchmark the returns are compared against.
    public string BenchmarkTicker { get; set; }

    // Pre-computed indicator series (same length as Prices, null-padded at start)
    public List<decimal?> Sma20 { get; set; } = [];
    public List<decimal?> Sma50 { get; set; } = [];
    public List<decimal?> Sma200 { get; set; } = [];
    public List<decimal?> Rsi14 { get; set; } = [];
    public List<decimal?> MacdLine { get; set; } = [];
    public List<decimal?> MacdSignal { get; set; } = [];
    public List<decimal?> MacdHistogram { get; set; } = [];

    // Bollinger Bands (20, 2) upper/lower envelope. The middle band equals Sma20,
    // so only the envelope is carried to avoid drawing a duplicate line.
    public List<decimal?> BollingerUpper { get; set; } = [];
    public List<decimal?> BollingerLower { get; set; } = [];

    // Technical signals surfaced as badges near the top of the tab.
    public MovingAverageCrossSignal MaCross { get; set; }
    public int PriceStreakDays { get; set; }
    public PriceStreakDirection PriceStreakDirection { get; set; }
}
