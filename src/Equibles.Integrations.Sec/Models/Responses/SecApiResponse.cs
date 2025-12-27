using Newtonsoft.Json;

namespace Equibles.Integrations.Sec.Models.Responses;

internal class SecApiResponse {
    [JsonProperty("cik")] public string Cik { get; set; }

    [JsonProperty("name")] public string Name { get; set; }

    [JsonProperty("entityType")] public string EntityType { get; set; }

    [JsonProperty("filings")] public FilingsContainer Filings { get; set; } = new();
}