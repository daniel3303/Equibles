using Newtonsoft.Json;

namespace Equibles.Integrations.Sec.Models.Responses;

/// <summary>
/// A single reported data point for a concept+unit. <see cref="Start"/> is
/// present only for duration facts (income statement / cash flow); instant
/// facts (balance sheet) carry <see cref="End"/> only.
/// </summary>
public class CompanyFactValue
{
    [JsonProperty("start")]
    public DateOnly? Start { get; set; }

    [JsonProperty("end")]
    public DateOnly End { get; set; }

    [JsonProperty("val")]
    public decimal Val { get; set; }

    [JsonProperty("accn")]
    public string Accn { get; set; }

    [JsonProperty("fy")]
    public int? Fy { get; set; }

    [JsonProperty("fp")]
    public string Fp { get; set; }

    [JsonProperty("form")]
    public string Form { get; set; }

    [JsonProperty("filed")]
    public DateOnly Filed { get; set; }

    [JsonProperty("frame")]
    public string Frame { get; set; }
}
