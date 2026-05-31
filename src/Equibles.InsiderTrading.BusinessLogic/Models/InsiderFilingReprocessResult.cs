namespace Equibles.InsiderTrading.BusinessLogic.Models;

/// <summary>
/// Progress/outcome of a version-driven insider-filing reprocess pass. Filings
/// whose transactions sit below the current parser version are re-parsed from
/// their cached ownership XML (fetched and cached on first encounter), which
/// re-derives <c>SecurityKind</c> from the source table and re-runs price
/// validity from the as-filed price.
/// </summary>
public class InsiderFilingReprocessResult
{
    /// <summary>Distinct filings needing reprocess at the start of the run.</summary>
    public int Total { get; set; }

    /// <summary>Filings processed so far this run (including failures).</summary>
    public int Processed { get; set; }

    /// <summary>Filings whose XML had to be fetched from EDGAR (not already cached).</summary>
    public int Fetched { get; set; }

    /// <summary>Rows whose SecurityKind changed (authoritatively reclassified).</summary>
    public int Reclassified { get; set; }

    /// <summary>Rows whose price was repaired (total ÷ shares) this run.</summary>
    public int Repaired { get; set; }

    /// <summary>Filings that could not be fetched or parsed this run.</summary>
    public int Failed { get; set; }

    public string Summary =>
        $"Reprocessed {Processed:N0}/{Total:N0} filings "
        + $"({Fetched:N0} fetched, {Reclassified:N0} rows reclassified, "
        + $"{Repaired:N0} prices repaired, {Failed:N0} failed).";
}
