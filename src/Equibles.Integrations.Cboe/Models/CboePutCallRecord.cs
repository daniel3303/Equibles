namespace Equibles.Integrations.Cboe.Models;

public class CboePutCallRecord {
    public DateOnly Date { get; set; }
    public long? CallVolume { get; set; }
    public long? PutVolume { get; set; }
    public long? TotalVolume { get; set; }
    public decimal? PutCallRatio { get; set; }
}
