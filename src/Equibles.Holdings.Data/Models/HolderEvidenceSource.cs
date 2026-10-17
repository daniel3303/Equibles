using System.ComponentModel.DataAnnotations;

namespace Equibles.Holdings.Data.Models;

/// <summary>The regulatory record a piece of holder classification evidence comes from.</summary>
public enum HolderEvidenceSource
{
    [Display(Name = "Form ADV roster")]
    FormAdvRoster = 0,

    [Display(Name = "SEC filer profile")]
    SecFilerProfile = 1,

    [Display(Name = "Schedule 13D")]
    Schedule13D = 2,

    [Display(Name = "Schedule 13G")]
    Schedule13G = 3,

    [Display(Name = "Form N-CEN")]
    NCenRegistrant = 4,
}
