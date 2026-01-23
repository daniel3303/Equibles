using Newtonsoft.Json;

namespace Equibles.Integrations.Finra.Models;

public class ShortVolumeRecord {
    [JsonProperty("tradeReportDate")]
    public string TradeReportDate { get; set; }

    [JsonProperty("securitiesInformationProcessorSymbolIdentifier")]
    public string Symbol { get; set; }

    [JsonProperty("shortParQuantity")]
    public long? ShortVolume { get; set; }

    [JsonProperty("shortExemptParQuantity")]
    public long? ShortExemptVolume { get; set; }

    [JsonProperty("totalParQuantity")]
    public long? TotalVolume { get; set; }

    [JsonProperty("marketCode")]
    public string MarketCode { get; set; }
}
