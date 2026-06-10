using Newtonsoft.Json;

namespace Equibles.Integrations.Fred.Models;

public class FredReleasesResponse
{
    [JsonProperty("releases")]
    public List<FredReleaseRecord> Releases { get; set; } = [];
}

public class FredReleaseRecord
{
    [JsonProperty("id")]
    public int Id { get; set; }

    [JsonProperty("name")]
    public string Name { get; set; }

    [JsonProperty("press_release")]
    public bool PressRelease { get; set; }

    [JsonProperty("link")]
    public string Link { get; set; }

    [JsonProperty("notes")]
    public string Notes { get; set; }
}
