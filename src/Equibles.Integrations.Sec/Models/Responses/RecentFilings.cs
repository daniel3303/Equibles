using Newtonsoft.Json;

namespace Equibles.Integrations.Sec.Models.Responses;

internal class RecentFilings
{
    [JsonProperty("accessionNumber")]
    public List<string> AccessionNumber { get; set; } = [];

    [JsonProperty("filingDate")]
    public List<string> FilingDate { get; set; } = [];

    [JsonProperty("reportDate")]
    public List<string> ReportDate { get; set; } = [];

    [JsonProperty("form")]
    public List<string> Form { get; set; } = [];

    [JsonProperty("primaryDocument")]
    public List<string> PrimaryDocument { get; set; } = [];

    [JsonProperty("primaryDocDescription")]
    public List<string> PrimaryDocDescription { get; set; } = [];

    // Comma-joined item numbers for the filing, e.g. "2.02,9.01" on an 8-K. Only
    // 8-K / 8-K/A rows carry items; the array is empty (or short) for other forms.
    [JsonProperty("items")]
    public List<string> Items { get; set; } = [];

    // The SEC file number EDGAR indexes the filing under, e.g. "001-35727"; a business
    // development company's reports carry its "814-" Investment Company Act number here even
    // when its cover page states an Exchange Act number.
    [JsonProperty("fileNumber")]
    public List<string> FileNumber { get; set; } = [];
}
