using System.ComponentModel.DataAnnotations;

namespace Equibles.Holdings.Data.Models;

public enum FilingType
{
    [Display(Name = "Form 13F")]
    Form13F,

    [Display(Name = "Schedule 13D")]
    Schedule13D,

    [Display(Name = "Schedule 13G")]
    Schedule13G,
}
