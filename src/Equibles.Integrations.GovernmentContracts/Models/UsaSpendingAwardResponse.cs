using Newtonsoft.Json;

namespace Equibles.Integrations.GovernmentContracts.Models;

public class UsaSpendingAwardResponse
{
    [JsonProperty("results")]
    public List<UsaSpendingAwardRecord> Results { get; set; } = [];

    [JsonProperty("page_metadata")]
    public UsaSpendingPageMetadata PageMetadata { get; set; }
}
