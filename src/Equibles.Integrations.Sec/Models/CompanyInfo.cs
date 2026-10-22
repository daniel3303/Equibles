using Newtonsoft.Json;

namespace Equibles.Integrations.Sec.Models;

public class CompanyInfo
{
    [JsonProperty("cik")]
    public string Cik { get; set; }

    [JsonProperty("name")]
    public string Name { get; set; }

    [JsonProperty("tickers")]
    public List<string> Tickers { get; set; } = [];

    /// <summary>
    /// The exchange the ticker directory states for each of <see cref="Tickers"/>, keyed by the
    /// ticker as the directory writes it ("Nasdaq", "NYSE", "OTC", ...).
    /// </summary>
    [JsonIgnore]
    public Dictionary<string, string> TickerExchanges { get; set; } = new(StringComparer.Ordinal);

    public string EntityType { get; set; }

    public bool IsOperatingCompany =>
        string.Equals(EntityType, "operating", StringComparison.OrdinalIgnoreCase);
}
