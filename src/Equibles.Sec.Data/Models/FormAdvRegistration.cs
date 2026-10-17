using System.ComponentModel.DataAnnotations;

namespace Equibles.Sec.Data.Models;

/// <summary>Which SEC Form ADV roster an adviser was last listed in.</summary>
public enum FormAdvRegistration
{
    [Display(Name = "SEC-registered adviser")]
    Registered = 0,

    [Display(Name = "Exempt reporting adviser")]
    ExemptReporting = 1,
}
