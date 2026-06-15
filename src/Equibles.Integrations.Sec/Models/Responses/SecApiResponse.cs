using Newtonsoft.Json;

namespace Equibles.Integrations.Sec.Models.Responses;

internal class SecApiResponse
{
    [JsonProperty("cik")]
    public string Cik { get; set; }

    [JsonProperty("name")]
    public string Name { get; set; }

    [JsonProperty("entityType")]
    public string EntityType { get; set; }

    /// <summary>
    /// SEC's 4-digit Standard Industrial Classification code (e.g. "3571" for
    /// Apple, "6221" for commodity-pool ETPs, "6726" for investment offices).
    /// Null/blank for filers SEC never classified.
    /// </summary>
    [JsonProperty("sic")]
    public string Sic { get; set; }

    [JsonProperty("exchanges")]
    public List<string> Exchanges { get; set; } = [];

    /// <summary>
    /// SEC's reported current fiscal year-end as a 4-character "MMDD" string
    /// (e.g. "0928" for Apple, "0630" for Microsoft, "1231" for calendar-year
    /// filers). May be null/blank for filers that never reported one.
    /// </summary>
    [JsonProperty("fiscalYearEnd")]
    public string FiscalYearEnd { get; set; }

    [JsonProperty("website")]
    public string Website { get; set; }

    [JsonProperty("filings")]
    public FilingsContainer Filings { get; set; } = new();
}
