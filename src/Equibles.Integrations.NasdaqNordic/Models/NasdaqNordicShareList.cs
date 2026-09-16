namespace Equibles.Integrations.NasdaqNordic.Models;

public sealed class NasdaqNordicShareList
{
    public Uri SourceUrl { get; set; }
    public string MarketCode { get; set; }
    public NasdaqNordicCategory Category { get; set; }
    public DateTime CapturedAt { get; set; }
    public List<NasdaqNordicShare> Shares { get; set; } = [];
    public string Json { get; set; }
}
