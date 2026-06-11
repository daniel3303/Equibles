using System.ComponentModel.DataAnnotations;

namespace Equibles.Congress.Data.Models;

public enum CongressionalDisclosureLineKind
{
    [Display(Name = "Asset")]
    Asset,

    [Display(Name = "Liability")]
    Liability,
}
