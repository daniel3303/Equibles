namespace Equibles.Sec.Data.Contracts;

/// <summary>
/// Shared shape for issuer-attributed SEC filings keyed by issuer, accession number and
/// filing date. Lets <c>SecFilingRepositoryBase&lt;TFiling&gt;</c> provide the common
/// by-issuer / by-accession / recent queries without duplicating them per filing type.
/// </summary>
public interface IIssuerFiling
{
    Guid EquityIssuerId { get; set; }
    string AccessionNumber { get; set; }
    DateOnly FilingDate { get; set; }
}
