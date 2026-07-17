using Newtonsoft.Json;

namespace Equibles.Integrations.GovernmentContracts.Models;

/// <summary>
/// One contract award row as returned by USAspending's
/// <c>POST /api/v2/search/spending_by_award/</c> endpoint. Field names mirror the
/// API's display labels verbatim; values are kept as wire strings and mapped to the
/// domain entity downstream.
/// </summary>
public class UsaSpendingAwardRecord
{
    /// <summary>The globally-unique award slug used by award-detail URLs.</summary>
    [JsonProperty("generated_internal_id")]
    public string GeneratedInternalId { get; set; }

    [JsonProperty("Award ID")]
    public string AwardId { get; set; }

    [JsonProperty("Recipient Name")]
    public string RecipientName { get; set; }

    [JsonProperty("recipient_id")]
    public string RecipientId { get; set; }

    [JsonProperty("Award Amount")]
    public decimal? Amount { get; set; }

    [JsonProperty("Total Outlays")]
    public decimal? TotalOutlays { get; set; }

    [JsonProperty("Awarding Agency")]
    public string AwardingAgency { get; set; }

    [JsonProperty("Contract Award Type")]
    public string ContractAwardType { get; set; }

    /// <summary>
    /// The date the base award was signed/first obligated — the award's action date.
    /// Unlike <see cref="StartDate"/> (period-of-performance start, which can sit years
    /// in the future), this is never a future date.
    /// </summary>
    [JsonProperty("Base Obligation Date")]
    public string BaseObligationDate { get; set; }

    [JsonProperty("Start Date")]
    public string StartDate { get; set; }

    [JsonProperty("End Date")]
    public string EndDate { get; set; }

    [JsonProperty("Last Modified Date")]
    public string LastModifiedDate { get; set; }

    [JsonProperty("NAICS")]
    [JsonConverter(typeof(UsaSpendingCodeConverter))]
    public string Naics { get; set; }

    [JsonProperty("PSC")]
    [JsonConverter(typeof(UsaSpendingCodeConverter))]
    public string Psc { get; set; }

    [JsonProperty("Description")]
    public string Description { get; set; }
}
