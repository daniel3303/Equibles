using System.ComponentModel.DataAnnotations;

namespace Equibles.Sec.Data.Models;

/// <summary>Form ADV Part 1A Item 2.B, why an exempt reporting adviser is exempt. Item (n) is bit n-1.</summary>
[Flags]
public enum FormAdvExemptionBasis
{
    None = 0,

    [Display(Name = "Adviser solely to venture capital funds")]
    VentureCapitalFundAdviser = 1 << 0,

    [Display(Name = "Private fund adviser under $150 million")]
    PrivateFundAdviserUnder150Million = 1 << 1,

    [Display(Name = "Private fund adviser at or above $150 million")]
    PrivateFundAdviserAtOrAbove150Million = 1 << 2,
}
