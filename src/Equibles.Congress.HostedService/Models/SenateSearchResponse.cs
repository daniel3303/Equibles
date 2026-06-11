using Newtonsoft.Json;

namespace Equibles.Congress.HostedService.Models;

/// <summary>
/// The eFD search endpoint's DataTables-style JSON envelope: each data row is
/// a list of cell strings (first name, last name, office, report link HTML,
/// date submitted).
/// </summary>
internal class SenateSearchResponse
{
    [JsonProperty("draw")]
    public int Draw { get; set; }

    [JsonProperty("recordsTotal")]
    public int RecordsTotal { get; set; }

    [JsonProperty("recordsFiltered")]
    public int RecordsFiltered { get; set; }

    [JsonProperty("data")]
    public List<List<string>> Data { get; set; } = [];

    [JsonProperty("result")]
    public string Result { get; set; }
}
