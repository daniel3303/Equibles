using System.ComponentModel.DataAnnotations;

namespace Equibles.Sec.Data.Models;

/// <summary>The private fund types of Form ADV Schedule D 7.B.(1) Question 10.</summary>
public enum FormAdvPrivateFundType
{
    [Display(Name = "Hedge funds")]
    Hedge = 0,

    [Display(Name = "Private equity funds")]
    PrivateEquity = 1,

    [Display(Name = "Venture capital funds")]
    VentureCapital = 2,

    [Display(Name = "Real estate funds")]
    RealEstate = 3,

    [Display(Name = "Liquidity funds")]
    Liquidity = 4,

    [Display(Name = "Securitized asset funds")]
    Securitized = 5,

    [Display(Name = "Other private funds")]
    Other = 6,
}
