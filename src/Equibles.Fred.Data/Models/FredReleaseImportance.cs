using System.ComponentModel.DataAnnotations;

namespace Equibles.Fred.Data.Models;

/// <summary>
/// How market-moving a release's scheduled print is, so calendar consumers can separate
/// the tier-1 announcements (CPI, jobs, GDP) from the daily rate and market-level updates
/// that would otherwise drown them out. Stamped on <see cref="FredRelease"/> by the
/// calendar importer from the curated per-release map; releases without a curated entry
/// default to Low.
/// </summary>
public enum FredReleaseImportance
{
    [Display(Name = "Low")]
    Low = 0,

    [Display(Name = "Medium")]
    Medium = 1,

    [Display(Name = "High")]
    High = 2,
}
