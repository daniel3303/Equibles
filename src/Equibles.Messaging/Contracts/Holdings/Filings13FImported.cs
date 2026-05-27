namespace Equibles.Messaging.Contracts.Holdings;

// Raised after a successful 13F import for each distinct ReportDate that
// received new filings. Triggers a per-quarter rebuild of the AUM and
// sector-allocation snapshots that power /holdings/stats and /holdings/trends.
public record Filings13FImported(DateOnly ReportDate, int FilingCount);
