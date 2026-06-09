using Newtonsoft.Json;

namespace Equibles.Integrations.Finra.Models;

public class OffExchangeWeeklyRecord
{
    [JsonProperty("issueSymbolIdentifier")]
    public string Symbol { get; set; }

    [JsonProperty("weekStartDate")]
    public string WeekStartDate { get; set; }

    [JsonProperty("summaryTypeCode")]
    public string SummaryTypeCode { get; set; }

    [JsonProperty("totalWeeklyShareQuantity")]
    public long? TotalWeeklyShareQuantity { get; set; }

    [JsonProperty("totalWeeklyTradeCount")]
    public long? TotalWeeklyTradeCount { get; set; }

    [JsonProperty("tierIdentifier")]
    public string TierIdentifier { get; set; }
}
