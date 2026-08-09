using Newtonsoft.Json;

namespace Equibles.Integrations.GovernmentContracts.Models;

/// <summary>
/// The slice of USAspending's <c>GET /api/v2/recipient/{recipient_id}/</c> profile this
/// integration needs: the recipient's registered corporate family. The parent linkage comes
/// from SAM registration data, so it is authoritative, not inferred. The top-level parent
/// fields carry the CURRENT linkage; <see cref="Parents"/> is the full registration
/// history and can name several distinct parents when ownership moved.
/// </summary>
public class UsaSpendingRecipientProfile
{
    [JsonProperty("recipient_id")]
    public string RecipientId { get; set; }

    [JsonProperty("name")]
    public string Name { get; set; }

    /// <summary>"R" (recipient), "P" (parent) or "C" (child) — the hash's level qualifier.</summary>
    [JsonProperty("recipient_level")]
    public string RecipientLevel { get; set; }

    [JsonProperty("parent_id")]
    public string ParentId { get; set; }

    [JsonProperty("parent_name")]
    public string ParentName { get; set; }

    /// <summary>
    /// The parent's DUNS — the most stable registrant identity across renames and the
    /// DUNS→UEI migration, so it is the primary key for deciding whether two parent
    /// entries are the SAME registrant or genuinely different owners.
    /// </summary>
    [JsonProperty("parent_duns")]
    public string ParentDuns { get; set; }

    [JsonProperty("parent_uei")]
    public string ParentUei { get; set; }

    /// <summary>
    /// Every parent SAM has registered for this recipient; can exceed one when ownership
    /// changed over the recipient's registration history.
    /// </summary>
    [JsonProperty("parents")]
    public List<UsaSpendingRecipientParentRef> Parents { get; set; } = [];
}
