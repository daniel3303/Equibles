namespace Equibles.Yahoo.HostedService.Services;

internal readonly record struct PriceSeriesTarget(
    string Ticker,
    Guid EquityIssuerId,
    Guid EquityListingId,
    bool IsPrimary,
    bool RequiresFullHistory = false,
    DateTime? YahooEnrichmentAttemptedAt = null,
    bool IsHistorical = false,
    DateOnly? HistoryEndDate = null,
    Guid? HistoricalEvidenceId = null,
    string MarketCountryCode = "US",
    string MarketIdentifierCode = null,
    string Isin = null
)
{
    public bool IsUs => MarketCountryCode == "US";
    public string ProviderSymbol => YahooListingSource.ProviderSymbol(this);
}
