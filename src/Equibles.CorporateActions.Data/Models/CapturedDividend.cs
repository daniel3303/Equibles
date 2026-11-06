namespace Equibles.CorporateActions.Data.Models;

// A source-neutral cash-dividend observation handed to
// CashDividendCaptureManager for upsert. Each capturing source maps its own
// payload to this DTO at its worker boundary (mirrors CapturedSplit), so the
// manager stays decoupled from any one integration.
public class CapturedDividend
{
    public DateOnly ExDate { get; set; }
    public decimal AmountPerShare { get; set; }
    public string Currency { get; set; }

    // Stated only by a source that carries the declaration (an exchange's record); null means unstated.
    public DateOnly? RecordDate { get; set; }
    public DateOnly? PaymentDate { get; set; }
    public CashDividendSource Source { get; set; }
}
