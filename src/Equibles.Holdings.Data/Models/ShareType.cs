using System.ComponentModel.DataAnnotations;

namespace Equibles.Holdings.Data.Models;

public enum ShareType {
    [Display(Name = "Shares")] Shares,
    [Display(Name = "Principal")] Principal
}
