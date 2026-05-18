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

    [JsonProperty("exchanges")]
    public List<string> Exchanges { get; set; } = [];

    /// <summary>
    /// SEC's reported current fiscal year-end as a 4-character "MMDD" string
    /// (e.g. "0928" for Apple, "0630" for Microsoft, "1231" for calendar-year
    /// filers). May be null/blank for filers that never reported one.
    /// </summary>
    [JsonProperty("fiscalYearEnd")]
    public string FiscalYearEnd { get; set; }

    [JsonProperty("filings")]
    public FilingsContainer Filings { get; set; } = new();
}
