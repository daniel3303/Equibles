using Newtonsoft.Json;

namespace Equibles.Integrations.Sec.Models.Responses;

internal class RecentFilings {
    [JsonProperty("accessionNumber")]
    public List<string> AccessionNumber { get; set; } = [];

    [JsonProperty("filingDate")]
    public List<string> FilingDate { get; set; } = [];

    [JsonProperty("reportDate")]
    public List<string> ReportDate { get; set; } = [];

    [JsonProperty("form")]
    public List<string> Form { get; set; } = [];

    [JsonProperty("primaryDocument")]
    public List<string> PrimaryDocument { get; set; } = [];

    [JsonProperty("primaryDocDescription")]
    public List<string> PrimaryDocDescription { get; set; } = [];
}