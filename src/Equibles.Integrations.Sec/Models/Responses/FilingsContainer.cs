using Newtonsoft.Json;

namespace Equibles.Integrations.Sec.Models.Responses;

internal class FilingsContainer {
    [JsonProperty("recent")]
    public RecentFilings Recent { get; set; } = new();

    [JsonProperty("files")]
    public List<FilingsArchiveFile> Files { get; set; } = [];
}