using System.Text.Json;
using Equibles.CommonStocks.Repositories;
using Equibles.Integrations.Yahoo.Models;

namespace Equibles.Yahoo.HostedService.Services;

internal static class YahooQuotationIdentity
{
    internal const string Source = "yahoo-chart-quotation-v1";

    public static bool HasUsDollarEvidence(string ticker, YahooChartSourceIdentity identity) =>
        !string.IsNullOrWhiteSpace(ticker)
        && identity is { Currency: "USD" }
        && !string.IsNullOrWhiteSpace(identity.ExchangeCode)
        && !string.IsNullOrWhiteSpace(identity.ExchangeTimeZone)
        && string.Equals(identity.Symbol, ticker, StringComparison.OrdinalIgnoreCase);

    public static Task<bool> Capture(
        EquityListingRepository repository,
        Guid issuerId,
        string ticker,
        YahooChartSourceIdentity identity,
        CancellationToken cancellationToken = default,
        Guid? expectedListingId = null
    )
    {
        if (!HasUsDollarEvidence(ticker, identity))
            return Task.FromResult(false);
        var payload = JsonSerializer.Serialize(
            new
            {
                EquityIssuerId = issuerId,
                RequestedTicker = ticker,
                SourceUrl = "https://query1.finance.yahoo.com/v8/finance/chart/"
                    + Uri.EscapeDataString(ticker),
                SourceIdentity = identity,
            }
        );
        return repository.RecordUsDollarQuotation(
            issuerId,
            ticker,
            Source,
            payload,
            cancellationToken,
            expectedListingId
        );
    }
}
