using Equibles.CommonStocks.Data.Models;

namespace Equibles.DelayedTrades.Repositories;

// The identity fields a print is matched and revalidated against; a projection so a market resolves in one query.
public sealed record DelayedTradeListingReference(
    Guid EquityListingId,
    Guid EquityIssuerId,
    string Isin,
    string MarketIdentifierCode,
    string Ticker,
    string TradingCurrency,
    decimal? QuoteUnitMultiplier,
    EquityIdentityState IdentityState,
    bool Active
);
