using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories.Models;
using Equibles.Integrations.Yahoo.Models;
using Newtonsoft.Json;

namespace Equibles.Yahoo.HostedService.Services;

internal static class YahooListingSource
{
    // Yahoo's published exchange suffix directory assigns .LS to Lisbon. A candidate
    // symbol alone is not a binding: returned chart metadata must also pass MatchesChart.
    internal static readonly string[] LisbonMarkets = ["XLIS", "ENXL", "ALXL"];
    internal const string LisbonEvidenceSource = "yahoo-lisbon-chart-v1";

    internal static bool IsLisbon(PriceSeriesTarget target) =>
        target.MarketCountryCode == "PT"
        && LisbonMarkets.Contains(target.MarketIdentifierCode)
        && !string.IsNullOrWhiteSpace(target.Isin);

    internal static string ProviderSymbol(PriceSeriesTarget target) =>
        target.IsUs ? target.Ticker
        : IsLisbon(target) ? target.Ticker + ".LS"
        : null;

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
            || IsLisbon(target)
                && !target.IsHistorical
                && listing.IdentityState == EquityIdentityState.Verified
                && listing.MarketIdentifierCode == target.MarketIdentifierCode
                && listing.Security.Isin == target.Isin
                && listing.TradingCurrency == "EUR"
                && listing.QuoteUnitMultiplier == 1m
        );

    internal static bool MatchesChart(
        PriceSeriesTarget target,
        YahooChartSourceIdentity identity
    ) =>
        IsLisbon(target)
        && identity
            is {
                Currency: "EUR",
                ExchangeCode: "LIS",
                InstrumentType: "EQUITY",
                ExchangeTimeZone: "Europe/Lisbon"
            }
        && string.Equals(identity.Symbol, ProviderSymbol(target), StringComparison.Ordinal);

    internal static EquityListingSourceBinding SourceBinding(PriceSeriesTarget target) =>
        new(
            target.EquityIssuerId,
            target.EquityListingId,
            target.Ticker,
            target.MarketIdentifierCode,
            target.MarketCountryCode,
            target.Isin,
            "EUR",
            1m,
            LisbonMarkets
        );

    internal static EquityListingQuotationEvidence QuotationEvidence(
        PriceSeriesTarget target,
        YahooChartSourceIdentity identity
    ) =>
        new(
            SourceBinding(target),
            LisbonEvidenceSource,
            JsonConvert.SerializeObject(
                new
                {
                    target.EquityIssuerId,
                    target.EquityListingId,
                    target.Ticker,
                    target.MarketIdentifierCode,
                    target.Isin,
                    RequestedSymbol = ProviderSymbol(target),
                    SourceMarkets = LisbonMarkets,
                    SourceUrl = "https://query1.finance.yahoo.com/v8/finance/chart/"
                        + Uri.EscapeDataString(ProviderSymbol(target)),
                    SourceIdentity = identity,
                }
            )
        );
}
