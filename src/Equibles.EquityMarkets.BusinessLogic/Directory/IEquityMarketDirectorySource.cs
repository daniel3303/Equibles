using Equibles.EquityMarkets.Data.Catalog;
using Equibles.EquityMarkets.Data.Models;

namespace Equibles.EquityMarkets.BusinessLogic.Directory;

public interface IEquityMarketDirectorySource
{
    string SourceKey { get; }

    bool Supports(EquityMarket market);

    Task<EquityMarketDirectorySnapshot> Capture(
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
