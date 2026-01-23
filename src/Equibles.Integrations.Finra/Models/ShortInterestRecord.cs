using Newtonsoft.Json;

namespace Equibles.Integrations.Finra.Models;

public class ShortInterestRecord {
    [JsonProperty("settlementDate")]
    public string SettlementDate { get; set; }

    [JsonProperty("symbolCode")]
    public string Symbol { get; set; }

    [JsonProperty("issueName")]
    public string IssueName { get; set; }

    [JsonProperty("currentShortPositionQuantity")]
    public long? CurrentShortPosition { get; set; }

    [JsonProperty("previousShortPositionQuantity")]
    public long? PreviousShortPosition { get; set; }

    [JsonProperty("changePreviousNumber")]
    public long? ChangeInShortPosition { get; set; }

    [JsonProperty("averageDailyVolumeQuantity")]
    public long? AverageDailyVolume { get; set; }

    [JsonProperty("daysToCoverQuantity")]
    public decimal? DaysToCover { get; set; }

    [JsonProperty("changePercent")]
    public decimal? ChangePercent { get; set; }

    [JsonProperty("marketClassCode")]
    public string MarketClassCode { get; set; }
}
