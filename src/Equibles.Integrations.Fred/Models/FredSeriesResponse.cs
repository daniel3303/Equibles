using Newtonsoft.Json;

namespace Equibles.Integrations.Fred.Models;

public class FredSeriesResponse {
    [JsonProperty("seriess")]
    public List<FredSeriesRecord> Series { get; set; } = [];
}

public class FredSeriesRecord {
    [JsonProperty("id")]
    public string Id { get; set; }

    [JsonProperty("title")]
    public string Title { get; set; }

    [JsonProperty("frequency")]
    public string Frequency { get; set; }

    [JsonProperty("frequency_short")]
    public string FrequencyShort { get; set; }

    [JsonProperty("units")]
    public string Units { get; set; }

    [JsonProperty("units_short")]
    public string UnitsShort { get; set; }

    [JsonProperty("seasonal_adjustment")]
    public string SeasonalAdjustment { get; set; }

    [JsonProperty("seasonal_adjustment_short")]
    public string SeasonalAdjustmentShort { get; set; }

    [JsonProperty("observation_start")]
    public string ObservationStart { get; set; }

    [JsonProperty("observation_end")]
    public string ObservationEnd { get; set; }

    [JsonProperty("last_updated")]
    public string LastUpdated { get; set; }

    [JsonProperty("notes")]
    public string Notes { get; set; }
}
