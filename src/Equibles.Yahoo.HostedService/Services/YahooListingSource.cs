using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories.Models;
using Equibles.EquityMarkets.Data.Catalog;
using Equibles.Integrations.Yahoo.Models;
using Newtonsoft.Json;

namespace Equibles.Yahoo.HostedService.Services;

internal static class YahooListingSource
{
    // The catalog's suffix is only a candidate symbol: returned chart metadata must also pass MatchesChart,
    // and the quotation unit the chart reports must be the one the verified listing states.
    internal static EquityMarket Market(PriceSeriesTarget target)
    {
        if (target.IsUs || string.IsNullOrWhiteSpace(target.Isin))
            return null;
        var market = EquityMarketCatalog.ByMarketIdentifierCode(target.MarketIdentifierCode);
        return market?.CountryCode == target.MarketCountryCode ? market : null;
    }

    internal static string EvidenceSource(EquityMarket market) => $"yahoo-{market.Code}-chart-v1";

    internal static string ProviderSymbol(PriceSeriesTarget target) =>
        target.IsUs ? target.Ticker
        : Market(target) is { } market ? target.Ticker + market.YahooSuffix
        : null;

    // The provider symbol for a listing named by its venue alone; a market outside the catalog has no Yahoo identity.
    internal static string ProviderSymbol(
        string ticker,
        string marketCountryCode,
        string marketIdentifierCode
    )
    {
        if (string.IsNullOrWhiteSpace(ticker))
            return null;
        if (marketCountryCode == "US")
            return ticker;
        var market = EquityMarketCatalog.ByMarketIdentifierCode(marketIdentifierCode);
        return market?.CountryCode == marketCountryCode ? ticker + market.YahooSuffix : null;
    }

    internal static bool MatchesListing(PriceSeriesTarget target, EquityListing listing) =>
        listing.Id == target.EquityListingId
        && listing.Security.EquityIssuerId == target.EquityIssuerId
        && listing.Ticker == target.Ticker
        && listing.MarketCountryCode == target.MarketCountryCode
        && (
            target.IsHistorical
                ? !listing.Active && listing.DelistedOn == target.HistoryEndDate
                : listing.Active
        )
        && (
            target.IsUs
            || Market(target) != null
                && !target.IsHistorical
                && listing.IdentityState == EquityIdentityState.Verified
                && listing.MarketIdentifierCode == target.MarketIdentifierCode
                && listing.Security.Isin == target.Isin
                && listing.TradingCurrency != null
                && listing.TradingCurrency == target.TradingCurrency
                && listing.QuoteUnitMultiplier != null
                && listing.QuoteUnitMultiplier == target.QuoteUnitMultiplier
        );

    internal static bool MatchesChart(
        PriceSeriesTarget target,
        YahooChartSourceIdentity identity
    ) =>
        Market(target) is { } market
        && identity != null
        && identity.ExchangeCode == market.YahooExchangeCode
        && identity.InstrumentType == "EQUITY"
        && identity.ExchangeTimeZone == market.TimeZoneId
        && EquityQuotationUnits.Matches(
            identity.Currency,
            target.TradingCurrency,
            target.QuoteUnitMultiplier
        )
        && string.Equals(identity.Symbol, ProviderSymbol(target), StringComparison.Ordinal);

    internal static EquityListingSourceBinding SourceBinding(PriceSeriesTarget target)
    {
        var market =
            Market(target)
            ?? throw new InvalidOperationException("Only catalog markets have a Yahoo binding.");
        return new(
            target.EquityIssuerId,
            target.EquityListingId,
            target.Ticker,
            target.MarketIdentifierCode,
            target.MarketCountryCode,
            target.Isin,
            target.TradingCurrency,
            target.QuoteUnitMultiplier
                ?? throw new InvalidOperationException(
                    "A catalog binding needs the listing's verified quotation unit."
                ),
            market.MarketIdentifierCodes.ToArray()
        );
    }

    internal static EquityListingQuotationEvidence QuotationEvidence(
        PriceSeriesTarget target,
        YahooChartSourceIdentity identity
    )
    {
        var market = Market(target);
        return new(
            SourceBinding(target),
            EvidenceSource(market),
            JsonConvert.SerializeObject(
                new
                {
                    target.EquityIssuerId,
                    target.EquityListingId,
                    target.Ticker,
                    target.MarketIdentifierCode,
                    target.Isin,
                    target.TradingCurrency,
                    target.QuoteUnitMultiplier,
                    Market = market.Code,
                    RequestedSymbol = ProviderSymbol(target),
                    SourceMarkets = market.MarketIdentifierCodes,
                    SourceUrl = "https://query1.finance.yahoo.com/v8/finance/chart/"
                        + Uri.EscapeDataString(ProviderSymbol(target)),
                    SourceIdentity = identity,
                }
            )
        );
    }
}
