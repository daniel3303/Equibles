namespace Equibles.CommonStocks.HostedService.Services;

/// <summary>
/// The result of probing a company for an investor-relations page, distinguishing a conclusive answer
/// from one the probe could not establish this cycle.
/// </summary>
public enum IrProbeOutcome
{
    /// <summary>A candidate validated as an IR page; see the accompanying result.</summary>
    Found,

    /// <summary>
    /// Every candidate was assessed — rendered real content that didn't validate, or was definitively
    /// absent — and none was an IR page. Conclusive: the stock can back off on the miss schedule.
    /// </summary>
    NoIrPageFound,

    /// <summary>
    /// At least one candidate couldn't be assessed because the stealth engine was unavailable (timeout,
    /// reaped/wedged sidecar), so a real IR page may have been missed. Transient: retry soon rather than
    /// writing the stock off for the full backoff.
    /// </summary>
    Inconclusive,
}
