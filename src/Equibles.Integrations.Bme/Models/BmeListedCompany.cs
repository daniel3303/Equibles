namespace Equibles.Integrations.Bme.Models;

// One company of the continuous market's listed-companies reply; it names one share line, the company's main one.
public sealed class BmeListedCompany
{
    public string Name { get; set; }
    public string CompanyKey { get; set; }
    public string Isin { get; set; }
    public string ShareName { get; set; }
    public string Sector { get; set; }
    public string Subsector { get; set; }
    public string TradingSystem { get; set; }
}
