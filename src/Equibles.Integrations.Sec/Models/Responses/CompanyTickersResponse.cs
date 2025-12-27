using Newtonsoft.Json;

namespace Equibles.Integrations.Sec.Models.Responses;

internal class CompanyTickersResponse {
    [JsonProperty("fields")]
    public List<string> Fields { get; set; } = [];

    [JsonProperty("data")]
    public List<List<object>> Data { get; set; } = [];
}