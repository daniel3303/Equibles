using System.ComponentModel.DataAnnotations;

namespace Equibles.Holdings.Data.Models;

/// <summary>The Rule 13d-1 paragraphs a Schedule 13G is filed under.</summary>
[Flags]
public enum Schedule13GRules
{
    [Display(Name = "None")]
    None = 0,

    [Display(Name = "Rule 13d-1(b), qualified institution")]
    QualifiedInstitution = 1 << 0,

    [Display(Name = "Rule 13d-1(c), passive investor")]
    PassiveInvestor = 1 << 1,

    [Display(Name = "Rule 13d-1(d), exempt investor")]
    ExemptInvestor = 1 << 2,
}
