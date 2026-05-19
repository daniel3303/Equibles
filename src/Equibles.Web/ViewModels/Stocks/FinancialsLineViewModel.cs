namespace Equibles.Web.ViewModels.Stocks;

/// <summary>
/// One rendered row of the selected statement. <see cref="HasValue"/> is false
/// when the company did not report that concept for the selected period — the
/// row still renders (so the statement keeps its shape) with a placeholder.
/// </summary>
public class FinancialsLineViewModel
{
    public string Label { get; set; }

    public bool HasValue { get; set; }

    public decimal Value { get; set; }

    public string Unit { get; set; }

    public DateOnly PeriodEnd { get; set; }

    public string Form { get; set; }

    public DateOnly FiledDate { get; set; }
}
