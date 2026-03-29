using Newtonsoft.Json;

namespace Equibles.Integrations.Yahoo.Models.Responses;

// Root: { "quoteSummary": { "result": [...], "error": null } }
public class YahooQuoteSummaryResponse {
    [JsonProperty("quoteSummary")]
    public QuoteSummaryContainer QuoteSummary { get; set; }
}

public class QuoteSummaryContainer {
    [JsonProperty("result")]
    public List<QuoteSummaryResult> Result { get; set; } = [];

    [JsonProperty("error")]
    public object Error { get; set; }
}

public class QuoteSummaryResult {
    [JsonProperty("recommendationTrend")]
    public RecommendationTrendContainer RecommendationTrend { get; set; }
}

public class RecommendationTrendContainer {
    [JsonProperty("trend")]
    public List<RecommendationTrendRecord> Trend { get; set; } = [];
}

public class RecommendationTrendRecord {
    [JsonProperty("period")]
    public string Period { get; set; }

    [JsonProperty("strongBuy")]
    public int StrongBuy { get; set; }

    [JsonProperty("buy")]
    public int Buy { get; set; }

    [JsonProperty("hold")]
    public int Hold { get; set; }

    [JsonProperty("sell")]
    public int Sell { get; set; }

    [JsonProperty("strongSell")]
    public int StrongSell { get; set; }
}
