using System.ComponentModel.DataAnnotations;

namespace Equibles.Sec.Data.Models;

/// <summary>
/// Tracks whether the raw XBRL envelope has been captured for a <see cref="Document"/>.
/// The default <see cref="NotChecked"/> is what a backfill looks for; <see cref="Captured"/>
/// and <see cref="NotPresent"/> are both terminal so a captured or genuinely XBRL-less
/// filing is never re-fetched.
/// </summary>
public enum XbrlCaptureStatus
{
    /// <summary>The filing has not yet been examined for an XBRL envelope (backfill target).</summary>
    [Display(Name = "Not checked")]
    NotChecked = 0,

    /// <summary>The XBRL envelope was captured and stored on the document.</summary>
    [Display(Name = "Captured")]
    Captured = 1,

    /// <summary>The filing was examined and carries no XBRL envelope — nothing to capture.</summary>
    [Display(Name = "Not present")]
    NotPresent = 2,
}
