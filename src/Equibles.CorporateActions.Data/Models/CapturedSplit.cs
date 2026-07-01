namespace Equibles.CorporateActions.Data.Models;

// A source-neutral split observation handed to StockSplitCaptureManager for
// upsert. Each capturing source (Yahoo, massive.com, SEC filings) maps its own
// payload to this DTO at its worker boundary, so the manager stays decoupled
// from any one integration. The ratio is Numerator:Denominator.
public class CapturedSplit
{
    public DateOnly EffectiveDate { get; set; }
    public decimal Numerator { get; set; }
    public decimal Denominator { get; set; }
    public StockSplitSource Source { get; set; }
}
