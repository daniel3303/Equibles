using Newtonsoft.Json;

namespace Equibles.Integrations.GovernmentContracts.Models;

public class UsaSpendingPageMetadata
{
    [JsonProperty("page")]
    public int Page { get; set; }

    [JsonProperty("hasNext")]
    public bool HasNext { get; set; }
}
