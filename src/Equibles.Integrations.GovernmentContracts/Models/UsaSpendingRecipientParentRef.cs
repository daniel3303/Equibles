using Newtonsoft.Json;

namespace Equibles.Integrations.GovernmentContracts.Models;

/// <summary>
/// One entry of a recipient profile's <c>parents[]</c> registration history. The same
/// registrant can appear several times under different names (legal renames) and under
/// different ids/UEIs (the DUNS→UEI migration re-hashed identities), so
/// <see cref="ParentDuns"/> is the identity to group by when comparing entries.
/// </summary>
public class UsaSpendingRecipientParentRef
{
    [JsonProperty("parent_id")]
    public string ParentId { get; set; }

    [JsonProperty("parent_name")]
    public string ParentName { get; set; }

    [JsonProperty("parent_duns")]
    public string ParentDuns { get; set; }

    [JsonProperty("parent_uei")]
    public string ParentUei { get; set; }
}
