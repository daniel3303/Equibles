using Equibles.EquityMarkets.Data.Catalog;
using Equibles.EquityMarkets.Data.Models;

namespace Equibles.EquityMarkets.BusinessLogic.Directory;

// Decides whether a directory row is the market's own listing of a share rather than a secondary quotation of one
// whose home is elsewhere, so every issuer gets one primary listing.
public static class EquityMarketDirectoryGate
{
    // FIRDS must record the ISIN as a live share on one of the market's venues. A directory that states the primary
    // market decides unless FIRDS places the share's home in another country; one that states none defers to FIRDS.
    // The share's other live FIRDS lines, when given, let the issuer's own request outrank a relevant venue it never sought.
    public static bool IsHomeShare(
        EquityMarket market,
        EquityMarketDirectoryRow row,
        FirdsInstrumentRecord firds,
        IReadOnlyCollection<FirdsInstrumentRecord> lines = null
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
        var requestedHere = IsHomedByIssuerRequest(market, firds, lines);
        if (row.StatedPrimaryMarketIdentifierCode == null)
            return market.IsHomeVenue(firds.RelevantTradingVenue) || requestedHere;
        // The authority escape admits a share FIRDS files on a regional venue of the same country, which is only
        // unambiguous while the catalog holds one market per country (pinned by EquityMarketCatalogTests).
        return market.IsHomeVenue(row.StatedPrimaryMarketIdentifierCode)
            && (
                market.IsHomeVenue(firds.RelevantTradingVenue)
                || firds.RelevantCompetentAuthority == market.CountryCode
                || requestedHere
            );
    }

    // FIRDS can name as relevant venue one the issuer never sought, such as a venue that traded a new or transferred
    // ISIN first. A line sought on this market is then the home, provided every line the issuer sought is on this
    // market and no other catalog market homes the share by its own lines; a null flag counts as sought, so it never
    // grants this. FirdsInstrumentRecordRepository.CountHomeShares counts by the same conditions.
    public static bool IsHomedByIssuerRequest(
        EquityMarket market,
        FirdsInstrumentRecord firds,
        IReadOnlyCollection<FirdsInstrumentRecord> lines
    )
    {
        ArgumentNullException.ThrowIfNull(market);
        if (
            lines == null
            || firds?.Isin is not { Length: >= 2 } isin
            || market.IsHomeVenue(firds.RelevantTradingVenue)
            || EquityMarketCatalog.IssuingCountriesHomedOutsideFirds.Contains(
                isin[..2],
                StringComparer.Ordinal
            )
        )
            return false;
        var register = lines
            .Where(line => line.Isin == isin && line.Authority == firds.Authority)
            .ToList();
        if (!register.Any(line => market.IsFirdsVenue(line.Mic) && line.IssuerRequested == true))
            return false;
        var relevant = register.Where(line => line.Mic == firds.RelevantTradingVenue).ToList();
        if (relevant.Count == 0 || relevant.Any(line => line.IssuerRequested != false))
            return false;
        if (
            register.Any(line =>
                line.IssuerRequested != false
                && !market.IsFirdsVenue(line.Mic)
                && !market.IsHomeVenue(line.Mic)
            )
        )
            return false;
        return !EquityMarketCatalog.All.Any(other =>
            other.Code != market.Code
            && other.FirdsAuthority != null
            && IsHomedByRelevantVenue(other, isin, lines)
        );
    }

    // The ordinary home CountHomeShares counts: a line of the market's register names one of its home venues as
    // relevant and, where the market requires it, the issuer sought (or did not deny) a home admission.
    private static bool IsHomedByRelevantVenue(
        EquityMarket market,
        string isin,
        IReadOnlyCollection<FirdsInstrumentRecord> lines
    )
    {
        var register = lines
            .Where(line => line.Isin == isin && line.Authority == market.FirdsAuthority)
            .ToList();
        return register.Any(line => market.IsHomeVenue(line.RelevantTradingVenue))
            && (
                !market.HomeRequiresIssuerRequest
                || register.Any(line =>
                    market.IsHomeVenue(line.Mic) && line.IssuerRequested != false
                )
            );
    }
}
