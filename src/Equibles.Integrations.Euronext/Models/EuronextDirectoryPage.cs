namespace Equibles.Integrations.Euronext.Models;

public class EuronextDirectoryPage
{
    public int TotalRecords { get; set; }
    public List<EuronextEquityListing> Listings { get; set; } = [];
}
