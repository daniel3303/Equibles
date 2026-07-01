namespace Equibles.Integrations.Yahoo.Models;

// A cash-dividend corporate action parsed from the chart endpoint's events
// node (events=div). Date is the ex-dividend date on the exchange-local
// calendar; Amount is the declared cash amount per share.
public class CashDividendEvent
{
    public DateOnly Date { get; set; }
    public decimal Amount { get; set; }
}
