using System.ComponentModel.DataAnnotations;

namespace Equibles.Holdings.Data.Models;

/// <summary>What decided a <see cref="PrivateFundFocus"/>.</summary>
public enum PrivateFundFocusBasis
{
    /// <summary>Fund counts by type from the monthly Form ADV roster.</summary>
    [Display(Name = "Fund count (Form ADV roster)")]
    FundCount = 0,

    /// <summary>Gross asset value by type from a Schedule D 7.B.(1) filing at most 24 months old.</summary>
    [Display(Name = "Gross assets (Schedule D 7.B.(1))")]
    GrossAssets = 1,
}
