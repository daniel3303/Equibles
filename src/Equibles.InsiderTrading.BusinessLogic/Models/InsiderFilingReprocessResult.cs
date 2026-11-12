namespace Equibles.InsiderTrading.BusinessLogic.Models;

/// <summary>
/// Progress/outcome of a version-driven insider-filing reprocess pass. Filings
/// whose transactions sit below the current parser version are re-parsed from
/// their cached ownership XML (fetched and cached on first encounter), which
/// re-derives transaction fields from the source and re-runs price validity
/// from the as-filed price.
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

    /// <summary>Rows whose implausible transaction date was corrected from source XML.</summary>
    public int DatesCorrected { get; set; }

    /// <summary>Rows whose Rule 10b5-1 checkbox value changed (v7 re-copies it — the v4
    /// capture parsed it but the reprocess never wrote it, so pre-capture rows stayed
    /// null).</summary>
    public int Rule10b5Stamped { get; set; }

    /// <summary>Rows moved to the issuer their ownership document names.</summary>
    public int Reattributed { get; set; }

    /// <summary>Rows removed because their document names an issuer outside the directory.</summary>
    public int ForeignIssuerRowsRemoved { get; set; }

    /// <summary>
    /// Rows removed so the import reads their filing again under the issuer it names, where an
    /// amendment or the original it restates waits to be paired with it.
    /// </summary>
    public int ReturnedToImport { get; set; }

    /// <summary>Filings that could not be fetched or parsed this run.</summary>
    public int Failed { get; set; }

    public string Summary =>
        FormattableString.Invariant(
            $"Reprocessed {Processed:N0}/{Total:N0} filings ({Fetched:N0} fetched, {Reclassified:N0} rows reclassified, {Repaired:N0} prices repaired, {DatesCorrected:N0} dates corrected, {Rule10b5Stamped:N0} 10b5-1 flags stamped, {Reattributed:N0} rows moved to their issuer, {ForeignIssuerRowsRemoved:N0} rows of an issuer outside the directory removed, {ReturnedToImport:N0} rows returned to the import, {Failed:N0} failed)."
        );
}
