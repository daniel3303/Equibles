namespace Equibles.Holdings.BusinessLogic.Ownership.Models;

/// <summary>The identifier a unified holder-list row is keyed on; never a name.</summary>
public enum StockHolderKeyKind
{
    Cik = 0,
    SeriesId = 1,
    Lei = 2,
    Opaque = 3,
}
