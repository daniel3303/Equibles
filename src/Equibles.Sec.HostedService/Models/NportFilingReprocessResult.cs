namespace Equibles.Sec.HostedService.Models;

/// <summary>
/// Progress/outcome of a version-driven NPORT-P reprocess pass. Filings whose
/// <see cref="Equibles.Sec.Data.Models.NportFiling.ParserVersion"/> sits below the current version
/// are re-fetched from EDGAR and re-parsed, which re-derives the schedule of portfolio holdings and
/// stamps the current version so the filing drops out of future passes.
/// </summary>
public class NportFilingReprocessResult
{
    /// <summary>Filings needing reprocess at the start of the run.</summary>
    public int Total { get; set; }

    /// <summary>Filings processed so far this run (excluding ones skipped after a failure).</summary>
    public int Processed { get; set; }

    /// <summary>Holdings inserted across all reprocessed filings this run.</summary>
    public int HoldingsAdded { get; set; }

    /// <summary>Filings that could not be fetched or parsed this run.</summary>
    public int Failed { get; set; }

    public string Summary =>
        FormattableString.Invariant(
            $"Reprocessed {Processed:N0}/{Total:N0} NPORT-P filings ({HoldingsAdded:N0} holdings added, {Failed:N0} failed)."
        );
}
