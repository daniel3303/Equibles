using System.ComponentModel.DataAnnotations;

namespace Equibles.InsiderTrading.Data.Models;

/// <summary>
/// Tracks whether the raw ownership XML for a Form 3/4/5 filing has been
/// captured into an <see cref="InsiderFiling"/>. Mirrors the proven XBRL
/// capture states on the SEC document: freshly-ingested filings are stored as
/// <see cref="Captured"/>; <see cref="NotChecked"/> leaves a filing as a
/// backfill target; <see cref="NotPresent"/> marks legacy pre-XML filings that
/// have no ownership document to store.
/// </summary>
public enum InsiderFilingCaptureStatus
{
    [Display(Name = "Not checked")]
    NotChecked = 0,

    [Display(Name = "Captured")]
    Captured = 1,

    [Display(Name = "Not present")]
    NotPresent = 2,
}
