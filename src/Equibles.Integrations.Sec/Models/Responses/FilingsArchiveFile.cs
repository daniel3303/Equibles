using Newtonsoft.Json;

namespace Equibles.Integrations.Sec.Models.Responses;

internal class FilingsArchiveFile {
    [JsonProperty("name")]
    public string Name { get; set; }

    [JsonProperty("filingCount")]
    public int FilingCount { get; set; }

    [JsonProperty("filingFrom")]
    public string FilingFrom { get; set; }

    [JsonProperty("filingTo")]
    public string FilingTo { get; set; }
}
