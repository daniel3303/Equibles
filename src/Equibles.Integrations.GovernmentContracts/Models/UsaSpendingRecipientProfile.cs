using Newtonsoft.Json;

namespace Equibles.Integrations.GovernmentContracts.Models;

/// <summary>
/// The slice of USAspending's <c>GET /api/v2/recipient/{recipient_id}/</c> profile this
/// integration needs: the recipient's registered corporate family. The parent linkage comes
/// from SAM registration data, so it is authoritative, not inferred.
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
    /// Every parent SAM has registered for this recipient; can exceed one when ownership
    /// changed over the recipient's registration history.
    /// </summary>
    [JsonProperty("parents")]
    public List<UsaSpendingRecipientParentRef> Parents { get; set; } = [];
}

public class UsaSpendingRecipientParentRef
{
    [JsonProperty("parent_id")]
    public string ParentId { get; set; }

    [JsonProperty("parent_name")]
    public string ParentName { get; set; }
}
