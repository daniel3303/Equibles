namespace Equibles.Integrations.Sec.Models;

public class CompanyMetadata
{
    public string Cik { get; set; }
    public string EntityType { get; set; }
    public List<string> Exchanges { get; set; } = [];

    public bool IsOperatingCompany =>
        string.Equals(EntityType, "operating", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// True when SEC's submissions document lists at least one real stock exchange.
    /// Subsidiaries that file with the SEC but aren't separately listed have an empty
    /// or OTC-only exchanges list, which is the strongest signal that they don't own
    /// the public ticker they share with their parent.
    /// </summary>
    public bool IsListed =>
        Exchanges.Any(e =>
            !string.IsNullOrWhiteSpace(e)
            && !string.Equals(e, "OTC", StringComparison.OrdinalIgnoreCase)
        );
}
