namespace Equibles.Holdings.BusinessLogic.Signals.Models;

/// <summary>A holder-overlap peer as surfaces show it.</summary>
public sealed record StockOverlapPeer(
    int Rank,
    Guid IssuerId,
    string Ticker,
    string Name,
    int SharedHolders,
    decimal Similarity
);
