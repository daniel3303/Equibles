namespace Equibles.Integrations.Yahoo.Models;

// A stock-split corporate action parsed from the chart endpoint's events node.
// The ratio is Numerator:Denominator (e.g. 10:1 forward, 1:12 reverse).
public class StockSplitEvent
{
    public DateOnly Date { get; set; }
    public decimal Numerator { get; set; }
    public decimal Denominator { get; set; }
}
