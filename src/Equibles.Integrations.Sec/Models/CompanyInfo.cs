using Newtonsoft.Json;

namespace Equibles.Integrations.Sec.Models;

public class CompanyInfo {
    [JsonProperty("cik")] public string Cik { get; set; }

    [JsonProperty("name")] public string Name { get; set; }

    [JsonProperty("tickers")] public List<string> Tickers { get; set; } = [];

    public string EntityType { get; set; }

    public bool IsOperatingCompany => string.Equals(EntityType, "operating", StringComparison.OrdinalIgnoreCase);
}
