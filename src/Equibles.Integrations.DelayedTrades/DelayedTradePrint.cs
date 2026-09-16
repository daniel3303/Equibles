namespace Equibles.Integrations.DelayedTrades;

// One published trade as the venue states it, flags kept verbatim so the filter rules stay readable.
public sealed record DelayedTradePrint(
    string Isin,
    string Venue,
    DateTime TradedAtUtc,
    DateTime PublishedAtUtc,
    decimal Price,
    decimal Quantity,
    string Currency,
    string PriceNotation,
    DelayedTradeModification Modification,
    string TradeId,
    string MarketMechanism,
    string BenchmarkIndicator,
    string ContributionToPrice,
    string NegotiationIndicator,
    bool MissingPrice,
    int LineNumber
);
