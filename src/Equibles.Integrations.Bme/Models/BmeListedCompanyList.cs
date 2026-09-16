namespace Equibles.Integrations.Bme.Models;

public sealed class BmeListedCompanyList
{
    public Uri SourceUrl { get; set; }
    public DateTime CapturedAt { get; set; }
    public int TotalResults { get; set; }
    public List<BmeListedCompany> Companies { get; set; } = [];
    public string Json { get; set; }
}
