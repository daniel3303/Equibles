using System.ComponentModel.DataAnnotations;

namespace Equibles.Holdings.Data.Models;

/// <summary>The private fund type making up more than half of an adviser's private funds by count (Form ADV Item 7.B).</summary>
public enum PrivateFundFocus
{
    [Display(Name = "None")]
    None = 0,

    [Display(Name = "Hedge funds")]
    Hedge = 1,

    [Display(Name = "Private equity funds")]
    PrivateEquity = 2,

    [Display(Name = "Venture capital funds")]
    VentureCapital = 3,

    [Display(Name = "Real estate funds")]
    RealEstate = 4,

    [Display(Name = "Liquidity funds")]
    Liquidity = 5,

    [Display(Name = "Securitized asset funds")]
    Securitized = 6,

    [Display(Name = "Other private funds")]
    Other = 7,

    [Display(Name = "Mixed")]
    Mixed = 8,
}
