using Newtonsoft.Json;

namespace Equibles.Integrations.Yahoo.Models.Responses;

// Root: { "quoteSummary": { "result": [...], "error": null } }
public class YahooQuoteSummaryResponse
{
    [JsonProperty("quoteSummary")]
    public QuoteSummaryContainer QuoteSummary { get; set; }
}

public class QuoteSummaryContainer
{
    [JsonProperty("result")]
    public List<QuoteSummaryResult> Result { get; set; } = [];

    [JsonProperty("error")]
    public object Error { get; set; }
}

public class QuoteSummaryResult
{
    [JsonProperty("recommendationTrend")]
    public RecommendationTrendContainer RecommendationTrend { get; set; }

    [JsonProperty("defaultKeyStatistics")]
    public DefaultKeyStatisticsContainer DefaultKeyStatistics { get; set; }

    [JsonProperty("assetProfile")]
    public AssetProfileContainer AssetProfile { get; set; }

    [JsonProperty("summaryDetail")]
    public SummaryDetailContainer SummaryDetail { get; set; }
}

public class SummaryDetailContainer
{
    [JsonProperty("marketCap")]
    public YahooRawValue MarketCap { get; set; }
}

public class AssetProfileContainer
{
    [JsonProperty("sector")]
    public string Sector { get; set; }

    [JsonProperty("industry")]
    public string Industry { get; set; }

    [JsonProperty("longBusinessSummary")]
    public string LongBusinessSummary { get; set; }

    [JsonProperty("website")]
    public string Website { get; set; }
}

public class DefaultKeyStatisticsContainer
{
    [JsonProperty("sharesOutstanding")]
    public YahooRawValue SharesOutstanding { get; set; }

    // The share base Yahoo builds summaryDetail.marketCap on: the entity-wide count with every
    // share class converted into the quoted listing's units. For a multi-class issuer this is
    // larger than sharesOutstanding (which covers only the quoted class).
    [JsonProperty("impliedSharesOutstanding")]
    public YahooRawValue ImpliedSharesOutstanding { get; set; }

    [JsonProperty("enterpriseValue")]
    public YahooRawValue EnterpriseValue { get; set; }
}

public class YahooRawValue
{
    [JsonProperty("raw")]
    public long Raw { get; set; }
}

public class RecommendationTrendContainer
{
    [JsonProperty("trend")]
    public List<RecommendationTrendRecord> Trend { get; set; } = [];
}

public class RecommendationTrendRecord
{
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
