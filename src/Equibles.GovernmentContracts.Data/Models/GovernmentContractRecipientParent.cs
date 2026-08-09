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
    /// A representative level-qualified hash for the parent registrant; null when the
    /// profile names no parent, names more than one distinct REGISTRANT (ownership moved —
    /// ambiguous, never guessed), or the recipient is unknown to the profile endpoint.
    /// </summary>
    [MaxLength(64)]
    public string ParentRecipientId { get; set; }

    /// <summary>
    /// Every name the parent registrant has carried (legal renames are one owner),
    /// newline-delimited; null under the same conditions as
    /// <see cref="ParentRecipientId"/>. Resolution tries each name through the exact
    /// normalised lookup, so whichever name our stock universe stores can match.
    /// </summary>
    [MaxLength(1024)]
    public string ParentNames { get; set; }

    /// <summary>When the profile was last fetched (UTC) — drives point-of-use re-resolution.</summary>
    public DateTime ResolvedAt { get; set; }

    /// <summary>
    /// True when this row records a profile fetch that kept FAILING server-side for this
    /// one recipient (USAspending 502s some individual profiles permanently — observed
    /// live on the first epoch-rescan window, where one such recipient wedged the whole
    /// lane for hours). A failure row answers "no usable parent" like a parentless row,
    /// but re-resolves on a much shorter staleness window because the answer is an
    /// unavailability, not a reading.
    /// </summary>
    public bool ProfileFetchFailed { get; set; }
}
