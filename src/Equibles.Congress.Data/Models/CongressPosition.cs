using System.ComponentModel.DataAnnotations;

namespace Equibles.Congress.Data.Models;

public enum CongressPosition {
    [Display(Name = "Representative")] Representative = 0,
    [Display(Name = "Senator")] Senator = 1
}
