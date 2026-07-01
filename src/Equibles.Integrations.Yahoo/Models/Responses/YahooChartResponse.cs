using Newtonsoft.Json;

namespace Equibles.Integrations.Yahoo.Models.Responses;

// Root: { "chart": { "result": [...], "error": null } }
public class YahooChartResponse
{
    [JsonProperty("chart")]
    public ChartContainer Chart { get; set; }
}

public class ChartContainer
{
    [JsonProperty("result")]
    public List<ChartResult> Result { get; set; } = [];

    [JsonProperty("error")]
    public object Error { get; set; }
}

public class ChartResult
{
    [JsonProperty("meta")]
    public ChartMeta Meta { get; set; }

    [JsonProperty("timestamp")]
    public List<long> Timestamp { get; set; } = [];

    [JsonProperty("indicators")]
    public ChartIndicators Indicators { get; set; }

    [JsonProperty("events")]
    public ChartEvents Events { get; set; }
}

public class ChartEvents
{
    // Keyed by the split's epoch-second string (e.g. "1718022600").
    [JsonProperty("splits")]
    public Dictionary<string, ChartSplit> Splits { get; set; } = [];
}

public class ChartSplit
{
    [JsonProperty("date")]
    public long Date { get; set; }

    [JsonProperty("numerator")]
    public decimal Numerator { get; set; }

    [JsonProperty("denominator")]
    public decimal Denominator { get; set; }

    [JsonProperty("splitRatio")]
    public string SplitRatio { get; set; }
}

public class ChartMeta
{
    // Exchange UTC offset in seconds. Yahoo stamps daily-bar timestamps in the
    // exchange's local time; this is how a UTC epoch maps back to the trading
    // day. Defaults to 0 when absent (UTC).
    [JsonProperty("gmtoffset")]
    public long GmtOffset { get; set; }

    [JsonProperty("exchangeTimezoneName")]
    public string ExchangeTimezoneName { get; set; }
}

public class ChartIndicators
{
    [JsonProperty("quote")]
    public List<ChartQuote> Quote { get; set; } = [];

    [JsonProperty("adjclose")]
    public List<ChartAdjClose> AdjClose { get; set; } = [];
}

public class ChartQuote
{
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

public class ChartAdjClose
{
    [JsonProperty("adjclose")]
    public List<decimal?> AdjustedClose { get; set; } = [];
}
