using Equibles.Holdings.Data.Models;

namespace Equibles.Holdings.BusinessLogic.Signals.Models;

/// <summary>A primary listing's signal row with its company, for market-wide rankings.</summary>
public sealed record StockSignalsRankingRow(
    StockQuarterlyHolderSignals Signals,
    string Ticker,
    string Name
);
