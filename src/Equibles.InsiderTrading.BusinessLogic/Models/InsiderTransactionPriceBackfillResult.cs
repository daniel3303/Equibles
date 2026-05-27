namespace Equibles.InsiderTrading.BusinessLogic.Models;

public class InsiderTransactionPriceBackfillResult
{
    public int Total { get; set; }
    public int Processed { get; set; }
    public int MarkedInvalid { get; set; }
    public int MarkedValid { get; set; }

    public string Summary =>
        Total == 0
            ? "No insider transactions to scan."
            : $"Scanned {Processed}/{Total} transactions. "
                + $"Marked invalid: {MarkedInvalid}. "
                + $"Marked valid: {MarkedValid}.";
}
