namespace Equibles.Integrations.Euronext.Models;

public class EuronextDirectorySnapshot
{
    public Uri SourceUrl { get; set; }
    public DateTime CapturedAt { get; set; }
    public List<EuronextEquityListing> Listings { get; set; } = [];
    public string DirectoryHtml { get; set; }
    public List<string> ResponseBodies { get; set; } = [];
}
