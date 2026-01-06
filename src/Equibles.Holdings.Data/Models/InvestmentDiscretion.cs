using System.ComponentModel.DataAnnotations;

namespace Equibles.Holdings.Data.Models;

public enum InvestmentDiscretion {
    [Display(Name = "Sole")] Sole,
    [Display(Name = "Defined")] Defined,
    [Display(Name = "Other")] Other
}
