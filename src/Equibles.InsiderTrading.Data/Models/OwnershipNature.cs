using System.ComponentModel.DataAnnotations;

namespace Equibles.InsiderTrading.Data.Models;

public enum OwnershipNature {
    [Display(Name = "Direct")] Direct,
    [Display(Name = "Indirect")] Indirect
}
