using Equibles.EquityMarkets.Data.Catalog;
using Equibles.EquityMarkets.Data.Models;

namespace Equibles.EquityMarkets.BusinessLogic.Directory;

// A venue's list of exchange-traded funds, commodities and notes, captured and reconciled apart from its share
// directory under its own source key, so neither list ever withdraws the other's listings.
public interface IExchangeTradedProductDirectorySource
{
    string SourceKey { get; }

    bool Supports(EquityMarket market);

    Task<EquityMarketDirectorySnapshot> CaptureProducts(
        EquityMarket market,
        CancellationToken cancellationToken
    );

    Task<EquityMarketDirectoryProduct> Resolve(
        EquityMarket market,
        EquityMarketDirectoryRow row,
        FirdsInstrumentRecord firds,
        CancellationToken cancellationToken
    );
}
