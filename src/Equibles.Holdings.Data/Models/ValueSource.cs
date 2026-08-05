namespace Equibles.Holdings.Data.Models;

/// <summary>
/// Where a holding's published <see cref="InstitutionalHolding.Value"/> came from.
/// </summary>
/// <remarks>
/// The derived figure (shares × closing price on a reconciled basis) is preferred because it is
/// comparable across filings, including Schedule 13D/G positions that carry no filed value at all.
/// When no honest derivation exists — the price series never yields a usable close, or the derived
/// figure grossly disagrees with the filer's own — the filer's reported value is published instead
/// of a zero that reads as "nothing". This column records which of the two the row carries, so
/// surfaces can disclose it and audits can count it.
/// </remarks>
public enum ValueSource
{
    /// <summary>Derived from shares × the report date's closing price (the default basis).</summary>
    Derived = 0,

    /// <summary>
    /// Copied from the filer's own reported market value (<see cref="InstitutionalHolding.FiledValue"/>)
    /// because no honest derivation was possible.
    /// </summary>
    Filed = 1,
}
