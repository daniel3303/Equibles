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
    Guid? HistoricalEvidenceId = null
);
