using System.ComponentModel.DataAnnotations;

namespace Equibles.Sec.Data.Models;

/// <summary>The role N-CEN Item C.9 / D.12 reports for a fund's adviser.</summary>
public enum NCenAdviserRole
{
    [Display(Name = "Investment Adviser")]
    InvestmentAdviser = 0,

    [Display(Name = "Sub-Adviser")]
    SubAdviser = 1,

    /// <summary>An investment adviser terminated during the reporting period.</summary>
    [Display(Name = "Terminated Investment Adviser")]
    TerminatedInvestmentAdviser = 2,

    /// <summary>A sub-adviser terminated during the reporting period.</summary>
    [Display(Name = "Terminated Sub-Adviser")]
    TerminatedSubAdviser = 3,
}
