namespace Equibles.EquityMarkets.BusinessLogic.Directory;

// A complete directory capture; the adapter refuses anything partial before this is constructed.
public sealed class EquityMarketDirectorySnapshot
{
    public string EvidenceSource { get; set; }
    public Uri SourceUrl { get; set; }
    public DateTime CapturedAt { get; set; }
    public string PayloadJson { get; set; }
    public List<EquityMarketDirectoryRow> Rows { get; set; } = [];
}
