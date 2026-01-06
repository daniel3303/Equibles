using System.ComponentModel.DataAnnotations;

namespace Equibles.Holdings.Data.Models;

public enum OptionType {
    [Display(Name = "Put")] Put,
    [Display(Name = "Call")] Call
}
