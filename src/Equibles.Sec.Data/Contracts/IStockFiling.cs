namespace Equibles.Sec.Data.Contracts;

/// <summary>
/// Shared shape for issuer-attributed SEC filings keyed by stock, accession number and
/// filing date. Lets <c>SecFilingRepositoryBase&lt;TFiling&gt;</c> provide the common
/// by-stock / by-accession / recent queries without duplicating them per filing type.
/// </summary>
public interface IStockFiling
{
    Guid CommonStockId { get; set; }
    string AccessionNumber { get; set; }
    DateOnly FilingDate { get; set; }
}
