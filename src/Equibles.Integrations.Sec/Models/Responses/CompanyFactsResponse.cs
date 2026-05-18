using Newtonsoft.Json;

namespace Equibles.Integrations.Sec.Models.Responses;

/// <summary>
/// Response of SEC's Company Facts API
/// (<c>https://data.sec.gov/api/xbrl/companyfacts/CIK##########.json</c>).
/// The <see cref="Facts"/> map is keyed by taxonomy (e.g. <c>us-gaap</c>,
/// <c>dei</c>) then by concept tag — those keys are data, not a fixed schema,
/// so a dictionary is the correct transport here.
/// </summary>
public class CompanyFactsResponse
{
    [JsonProperty("cik")]
    public long Cik { get; set; }

    [JsonProperty("entityName")]
    public string EntityName { get; set; }

    [JsonProperty("facts")]
    public Dictionary<string, Dictionary<string, CompanyFactConcept>> Facts { get; set; } = [];
}
