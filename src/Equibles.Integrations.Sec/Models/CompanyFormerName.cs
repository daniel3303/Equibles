using Newtonsoft.Json;

namespace Equibles.Integrations.Sec.Models;

public class CompanyFormerName
{
    [JsonProperty("name")]
    public string Name { get; set; }

    [JsonProperty("from")]
    public string From { get; set; }

    [JsonProperty("to")]
    public string To { get; set; }
}
