namespace Equibles.Holdings.BusinessLogic.Ownership.Models;

/// <summary>
/// A holder's identity across disclosures: a canonical CIK for 13F, 13D, 13G and insider filers,
/// an SEC series ID for a fund, an LEI for a non-US holder, or an opaque source key otherwise.
/// </summary>
public sealed record StockHolderKey(StockHolderKeyKind Kind, string Value)
{
    public override string ToString() =>
        $"{Kind switch
        {
            StockHolderKeyKind.Cik => "cik",
            StockHolderKeyKind.SeriesId => "series",
            StockHolderKeyKind.Lei => "lei",
            _ => "key",
        }}:{Value}";
}
