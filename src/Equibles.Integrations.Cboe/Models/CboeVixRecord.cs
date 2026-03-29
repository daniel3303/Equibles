namespace Equibles.Integrations.Cboe.Models;

public class CboeVixRecord {
    public DateOnly Date { get; set; }
    public decimal Open { get; set; }
    public decimal High { get; set; }
    public decimal Low { get; set; }
    public decimal Close { get; set; }
}
