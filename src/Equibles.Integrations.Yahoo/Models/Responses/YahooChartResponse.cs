using Newtonsoft.Json;

namespace Equibles.Integrations.Yahoo.Models.Responses;

// Root: { "chart": { "result": [...], "error": null } }
public class YahooChartResponse {
    [JsonProperty("chart")]
    public ChartContainer Chart { get; set; }
}

public class ChartContainer {
    [JsonProperty("result")]
    public List<ChartResult> Result { get; set; } = [];

    [JsonProperty("error")]
    public object Error { get; set; }
}

public class ChartResult {
    [JsonProperty("timestamp")]
    public List<long> Timestamp { get; set; } = [];

    [JsonProperty("indicators")]
    public ChartIndicators Indicators { get; set; }
}

public class ChartIndicators {
    [JsonProperty("quote")]
    public List<ChartQuote> Quote { get; set; } = [];

    [JsonProperty("adjclose")]
    public List<ChartAdjClose> AdjClose { get; set; } = [];
}

public class ChartQuote {
    [JsonProperty("open")]
    public List<decimal?> Open { get; set; } = [];

    [JsonProperty("high")]
    public List<decimal?> High { get; set; } = [];

    [JsonProperty("low")]
    public List<decimal?> Low { get; set; } = [];

    [JsonProperty("close")]
    public List<decimal?> Close { get; set; } = [];

    [JsonProperty("volume")]
    public List<long?> Volume { get; set; } = [];
}

public class ChartAdjClose {
    [JsonProperty("adjclose")]
    public List<decimal?> AdjustedClose { get; set; } = [];
}
