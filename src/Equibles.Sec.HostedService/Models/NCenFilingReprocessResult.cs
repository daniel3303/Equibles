namespace Equibles.Sec.HostedService.Models;

/// <summary>
/// Outcome of one N-CEN reprocess pass: filings below
/// <see cref="Equibles.Sec.Data.Models.NCenFiling.CurrentParserVersion"/> are re-read from EDGAR.
/// </summary>
public class NCenFilingReprocessResult
{
    /// <summary>Filings below the current parser version at the start of the pass.</summary>
    public int Total { get; set; }

    /// <summary>Filings re-read and stamped current this pass.</summary>
    public int Processed { get; set; }

    /// <summary>Filings whose fetch or parse failed this pass.</summary>
    public int Failed { get; set; }

    /// <summary>True when the pass stopped early because EDGAR was unavailable.</summary>
    public bool StoppedByOutage { get; set; }

    public string Summary =>
        FormattableString.Invariant(
            $"Reprocessed {Processed:N0}/{Total:N0} N-CEN filings ({Failed:N0} failed{(StoppedByOutage ? ", stopped by an EDGAR outage" : "")})."
        );
}
