using System.ComponentModel.DataAnnotations;

namespace Equibles.InsiderTrading.Data.Models;

/// <summary>
/// Whether an insider transaction concerns the issuer's actual shares or a
/// derivative instrument (option, warrant, convertible, etc.). Derived
/// authoritatively from which Form 4 table the row was parsed from —
/// <c>nonDerivativeTable</c> vs <c>derivativeTable</c> — not from the security
/// title text. <see cref="Unknown"/> marks rows that predate this capture and
/// haven't been reclassified yet.
/// </summary>
public enum InsiderSecurityKind
{
    [Display(Name = "Unknown")]
    Unknown = 0,

    [Display(Name = "Non-derivative")]
    NonDerivative = 1,

    [Display(Name = "Derivative")]
    Derivative = 2,
}
