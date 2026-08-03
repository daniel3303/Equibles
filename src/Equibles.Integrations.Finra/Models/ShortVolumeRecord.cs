using Newtonsoft.Json;

namespace Equibles.Integrations.Finra.Models;

public class ShortVolumeRecord
{
    [JsonProperty("tradeReportDate")]
    public string TradeReportDate { get; set; }

    [JsonProperty("securitiesInformationProcessorSymbolIdentifier")]
    public string Symbol { get; set; }

    [JsonProperty("shortParQuantity")]
    public decimal? ShortVolume { get; set; }

    [JsonProperty("shortExemptParQuantity")]
    public decimal? ShortExemptVolume { get; set; }

    [JsonProperty("totalParQuantity")]
    public decimal? TotalVolume { get; set; }

    [JsonProperty("marketCode")]
    public string MarketCode { get; set; }
}
