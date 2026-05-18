using Newtonsoft.Json;

namespace Equibles.Integrations.Sec.Models.Responses;

/// <summary>
/// One XBRL concept within a <see cref="CompanyFactsResponse"/>. <see cref="Units"/>
/// is keyed by unit string (e.g. <c>USD</c>, <c>USD/shares</c>, <c>shares</c>).
/// </summary>
public class CompanyFactConcept
{
    [JsonProperty("label")]
    public string Label { get; set; }

    [JsonProperty("description")]
    public string Description { get; set; }

    [JsonProperty("units")]
    public Dictionary<string, List<CompanyFactValue>> Units { get; set; } = [];
}
