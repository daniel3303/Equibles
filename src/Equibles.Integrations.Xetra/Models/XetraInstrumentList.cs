namespace Equibles.Integrations.Xetra.Models;

public sealed class XetraInstrumentList
{
    public Uri PageUrl { get; set; }
    public Uri SourceUrl { get; set; }
    public string MarketIdentifierCode { get; set; }
    public DateOnly LastUpdate { get; set; }
    public DateTime CapturedAt { get; set; }
    public List<XetraInstrument> Instruments { get; set; } = [];
    public string Csv { get; set; }
}
