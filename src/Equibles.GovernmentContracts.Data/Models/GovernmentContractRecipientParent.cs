using System.ComponentModel.DataAnnotations;

namespace Equibles.GovernmentContracts.Data.Models;

/// <summary>
/// Cached corporate-parent resolution for one USAspending recipient, filled from the
/// recipient-profile endpoint the first time an award's recipient name fails to match a
/// public company directly. SAM registration data links an operating subsidiary
/// ("Raytheon Company", "CACI Inc - Federal") to its registered parent, whose name is what
/// our CommonStock universe carries — so the parent name is re-resolved through the same
/// exact normalised-name lookup, never fuzzily.
///
/// A row with null parent fields is itself an answer ("this recipient has no usable
/// parent") and stops the profile from being re-fetched on every award; rows re-resolve at
/// point of use once <see cref="ResolvedAt"/> is older than the import's staleness window,
/// because acquisitions move subsidiaries between listed parents.
/// </summary>
public class GovernmentContractRecipientParent
{
    /// <summary>
    /// USAspending's level-qualified recipient hash (e.g. "abc123…-C"), as carried on award
    /// rows — the profile endpoint's key.
    /// </summary>
    [Key]
    [MaxLength(64)]
    public string RecipientId { get; set; }

    /// <summary>Recipient name as seen on the award that triggered the resolution.</summary>
    [MaxLength(512)]
    public string RecipientName { get; set; }

    /// <summary>
    /// The parent's level-qualified recipient hash; null when the profile names no parent,
    /// names more than one distinct parent (ambiguous — never guessed), or the recipient is
    /// unknown to the profile endpoint.
    /// </summary>
    [MaxLength(64)]
    public string ParentRecipientId { get; set; }

    /// <summary>The parent's registered name; null under the same conditions.</summary>
    [MaxLength(512)]
    public string ParentName { get; set; }

    /// <summary>When the profile was last fetched (UTC) — drives point-of-use re-resolution.</summary>
    public DateTime ResolvedAt { get; set; }
}
