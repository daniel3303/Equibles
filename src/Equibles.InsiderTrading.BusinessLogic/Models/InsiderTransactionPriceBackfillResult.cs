namespace Equibles.InsiderTrading.BusinessLogic.Models;

public class InsiderTransactionPriceBackfillResult
{
    /// <summary>Rows that needed evaluating this run (IsPriceValid was null).</summary>
    public int Total { get; set; }

    public int Processed { get; set; }

    /// <summary>Implausible rows repaired by dividing the reported total by Shares.</summary>
    public int Repaired { get; set; }

    /// <summary>Rows confirmed plausible — no change.</summary>
    public int Valid { get; set; }

    /// <summary>Implausible rows that couldn't be repaired (Shares == 0).</summary>
    public int Invalid { get; set; }

    /// <summary>Rows still without a usable close — left null for a later run.</summary>
    public int Pending { get; set; }

    public string Summary =>
        Total == 0
            ? "No unevaluated insider transactions to scan."
            : $"Evaluated {Processed}/{Total} transactions. "
                + $"Repaired: {Repaired}. Valid: {Valid}. "
                + $"Invalid (no share count): {Invalid}. "
                + $"Still pending (no close yet): {Pending}.";
}
