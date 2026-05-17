using Newtonsoft.Json;

namespace Equibles.Integrations.Sec.Models.Responses;

/// <summary>
/// Shape of a filing's <c>index.json</c>
/// (<c>/Archives/edgar/data/{cik}/{accession-no-dashes}/index.json</c>),
/// which lists every artifact inside a single filing.
/// </summary>
public class FilingIndexResponse
{
    [JsonProperty("directory")]
    public FilingIndexDirectory Directory { get; set; }
}

public class FilingIndexDirectory
{
    [JsonProperty("item")]
    public List<FilingIndexItem> Item { get; set; } = [];
}

public class FilingIndexItem
{
    [JsonProperty("name")]
    public string Name { get; set; }

    [JsonProperty("type")]
    public string Type { get; set; }
}
