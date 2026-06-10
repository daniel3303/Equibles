using Newtonsoft.Json;

namespace Equibles.Integrations.Fred.Models;

public class FredReleasesDatesResponse
{
    [JsonProperty("count")]
    public int Count { get; set; }

    [JsonProperty("offset")]
    public int Offset { get; set; }

    [JsonProperty("limit")]
    public int Limit { get; set; }

    [JsonProperty("release_dates")]
    public List<FredReleaseDateRecord> ReleaseDates { get; set; } = [];
}

public class FredReleaseDateRecord
{
    [JsonProperty("release_id")]
    public int ReleaseId { get; set; }

    [JsonProperty("release_name")]
    public string ReleaseName { get; set; }

    [JsonProperty("date")]
    public string Date { get; set; }
}
