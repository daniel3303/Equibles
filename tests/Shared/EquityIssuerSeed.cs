using Equibles.CommonStocks.Data.Helpers;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Data.Models.Taxonomies;

namespace Equibles.TestSupport;

// Test setup creates the real issuer/security/listing graph; production has no flat stock facade.
internal static class EquityIssuerSeed
{
    public static EquityIssuer Create(
        Guid? Id = null,
        string Ticker = null,
        bool Active = true,
        DateOnly? DelistedOn = null,
        DateTime? HistoricalPriceBackfillAttemptedAt = null,
        DateTime? HistoricalCusipBackfillRequestedAt = null,
        List<string> HistoricalCusipBackfillCandidates = null,
        DateOnly? HistoricalCusipBackfillCandidateOn = null,
        bool HistoricalCusipBackfillAmbiguous = false,
        DateTime? HistoricalCusipBackfillSweepStartedAt = null,
        string Name = null,
        string Description = null,
        string Cik = null,
        string Website = null,
        DateTime? WebsiteCheckedAt = null,
        DateTime? YahooEnrichmentAttemptedAt = null,
        double MarketCapitalization = 0,
        long SharesOutStanding = 0,
        ListedSecurityType ListedSecurityType = default,
        string ListedSecurityTitle = null,
        List<string> SecondaryTickers = null,
        List<string> ReferenceTickers = null,
        List<string> PriceHistoryBackfilledTickers = null,
        List<string> SecondaryCiks = null,
        string Cusip = null,
        int? FiscalYearEndMonth = null,
        int? FiscalYearEndDay = null,
        string Sic = null,
        string EntityType = null,
        Guid? IndustryId = null,
        Industry Industry = null,
        string LegalEntityIdentifier = null,
        string Isin = null,
        string MarketCountryCode = "US",
        string MarketIdentifierCode = null,
        EquityIdentityState IdentityState = EquityIdentityState.Legacy,
        string TradingCurrency = null,
        decimal? QuoteUnitMultiplier = null
    )
    {
        var issuer = new EquityIssuer
        {
            Id = Id ?? Guid.NewGuid(),
            Name = Name,
            Description = Description,
            Cik = Cik,
            LegalEntityIdentifier = LegalEntityIdentifier,
            Website = Website,
            WebsiteCheckedAt = WebsiteCheckedAt,
            SecondaryCiks = SecondaryCiks ?? [],
            FiscalYearEndMonth = FiscalYearEndMonth,
            FiscalYearEndDay = FiscalYearEndDay,
            Sic = Sic,
            EntityType = EntityType,
            IndustryId = IndustryId,
            Industry = Industry,
        };
        if (Ticker != null)
        {
            // Empty symbols remain possible in validation tests, before persistence. A venue
            // listing is built directly: the US directory helper hard-codes the US market.
            var primary =
                string.IsNullOrWhiteSpace(Ticker) || MarketCountryCode != "US"
                    ? AddPresentationListing(
                        issuer,
                        Ticker,
                        MarketCountryCode,
                        MarketIdentifierCode,
                        IdentityState,
                        TradingCurrency,
                        QuoteUnitMultiplier
                    )
                    : UsEquityDirectory.SelectPrimary(issuer, Ticker);
            primary.IsDirectoryListed = true;
            primary.Active = Active;
            primary.DelistedOn = DelistedOn;
            primary.HistoricalPriceBackfillAttemptedAt = HistoricalPriceBackfillAttemptedAt;
            primary.HistoricalCusipBackfillRequestedAt = HistoricalCusipBackfillRequestedAt;
            primary.HistoricalCusipBackfillCandidates = HistoricalCusipBackfillCandidates ?? [];
            primary.HistoricalCusipBackfillCandidateOn = HistoricalCusipBackfillCandidateOn;
            primary.HistoricalCusipBackfillAmbiguous = HistoricalCusipBackfillAmbiguous;
            primary.HistoricalCusipBackfillSweepStartedAt = HistoricalCusipBackfillSweepStartedAt;
            primary.YahooEnrichmentAttemptedAt = YahooEnrichmentAttemptedAt;

            primary.Security.Cusip = Cusip;
            primary.Security.Isin = Isin;
            primary.Security.MarketCapitalization = MarketCapitalization;
            primary.Security.SharesOutstanding = SharesOutStanding;
            primary.Security.RegistrationType = ListedSecurityType;
            primary.Security.RegistrationTitle = ListedSecurityTitle;
        }
        SetSecondaryTickers(issuer, SecondaryTickers ?? []);
        SetReferenceTickers(issuer, ReferenceTickers ?? []);
        SetPriceHistoryBackfilledTickers(issuer, PriceHistoryBackfilledTickers ?? []);
        return issuer;
    }

    public static void SetSecondaryTickers(EquityIssuer issuer, IEnumerable<string> symbols)
    {
        var tickers = symbols.ToHashSet(StringComparer.Ordinal);
        foreach (var ticker in tickers)
            UsEquityDirectory.GetOrAddListing(issuer, ticker);
        foreach (
            var listing in issuer
                .Securities.SelectMany(security => security.Listings)
                .Where(listing => listing.MarketCountryCode == "US")
        )
            listing.IsDirectoryListed =
                listing.Id == issuer.Presentation?.EquityListingId
                || tickers.Contains(listing.Ticker);
    }

    public static void SetReferenceTickers(EquityIssuer issuer, IEnumerable<string> symbols)
    {
        var tickers = symbols.ToHashSet(StringComparer.Ordinal);
        foreach (var ticker in tickers)
            UsEquityDirectory.GetOrAddListing(issuer, ticker);
        foreach (
            var listing in issuer
                .Securities.SelectMany(security => security.Listings)
                .Where(listing => listing.MarketCountryCode == "US")
        )
            listing.IsReferenceListed = tickers.Contains(listing.Ticker);
    }

    public static void SetPriceHistoryBackfilledTickers(
        EquityIssuer issuer,
        IEnumerable<string> symbols
    )
    {
        var tickers = symbols.ToHashSet(StringComparer.Ordinal);
        foreach (var ticker in tickers)
            UsEquityDirectory.GetOrAddListing(issuer, ticker);
        foreach (
            var listing in issuer
                .Securities.SelectMany(security => security.Listings)
                .Where(listing => listing.MarketCountryCode == "US")
        )
            listing.PriceHistoryBackfilled = tickers.Contains(listing.Ticker);
    }

    private static EquityListing AddPresentationListing(
        EquityIssuer issuer,
        string ticker,
        string marketCountryCode,
        string marketIdentifierCode,
        EquityIdentityState identityState,
        string tradingCurrency,
        decimal? quoteUnitMultiplier
    )
    {
        var security = new EquitySecurity { Issuer = issuer, EquityIssuerId = issuer.Id };
        var listing = new EquityListing
        {
            Security = security,
            EquitySecurityId = security.Id,
            Ticker = ticker,
            MarketCountryCode = marketCountryCode,
            MarketIdentifierCode = marketIdentifierCode,
            IdentityState = identityState,
            TradingCurrency = tradingCurrency,
            QuoteUnitMultiplier = quoteUnitMultiplier,
        };
        security.Listings.Add(listing);
        issuer.Securities.Add(security);
        issuer.Presentation = new EquityIssuerPresentation
        {
            Issuer = issuer,
            EquityIssuerId = issuer.Id,
            Listing = listing,
            EquityListingId = listing.Id,
        };
        return listing;
    }
}
