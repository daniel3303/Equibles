using Newtonsoft.Json;

namespace Equibles.Integrations.Fred.Models;

public class FredObservationsResponse {
    [JsonProperty("realtime_start")]
    public string RealtimeStart { get; set; }

    [JsonProperty("realtime_end")]
    public string RealtimeEnd { get; set; }

    [JsonProperty("observation_start")]
    public string ObservationStart { get; set; }

    [JsonProperty("observation_end")]
    public string ObservationEnd { get; set; }

    [JsonProperty("count")]
    public int Count { get; set; }

    [JsonProperty("offset")]
    public int Offset { get; set; }

    [JsonProperty("limit")]
    public int Limit { get; set; }

    [JsonProperty("observations")]
    public List<FredObservationRecord> Observations { get; set; } = [];
}

public class FredObservationRecord {
    [JsonProperty("date")]
    public string Date { get; set; }

    [JsonProperty("value")]
    public string Value { get; set; }
}
