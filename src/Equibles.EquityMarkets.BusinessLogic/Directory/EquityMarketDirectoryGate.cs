using Equibles.EquityMarkets.Data.Catalog;
using Equibles.EquityMarkets.Data.Models;

namespace Equibles.EquityMarkets.BusinessLogic.Directory;

// Decides whether a directory row is the market's own listing of a share rather than a secondary quotation of one
// whose home is elsewhere, so every issuer gets one primary listing.
public static class EquityMarketDirectoryGate
{
    // FIRDS must record the ISIN as a live share on one of the market's venues. A directory that states the primary
    // market decides unless FIRDS places the share's home in another country; one that states none defers to FIRDS.
    public static bool IsHomeShare(
        EquityMarket market,
        EquityMarketDirectoryRow row,
        FirdsInstrumentRecord firds
    )
    {
        ArgumentNullException.ThrowIfNull(market);
        ArgumentNullException.ThrowIfNull(row);
        if (
            firds == null
            || firds.Isin != row.Isin
            || !market.IsFirdsVenue(firds.Mic)
            || !market.Contains(row.MarketIdentifierCode)
        )
            return false;
        if (row.StatedPrimaryMarketIdentifierCode == null)
            return market.IsHomeVenue(firds.RelevantTradingVenue);
        // The authority escape admits a share FIRDS files on a regional venue of the same country, which is only
        // unambiguous while the catalog holds one market per country (pinned by EquityMarketCatalogTests).
        return market.IsHomeVenue(row.StatedPrimaryMarketIdentifierCode)
            && (
                market.IsHomeVenue(firds.RelevantTradingVenue)
                || firds.RelevantCompetentAuthority == market.CountryCode
            );
    }
}
