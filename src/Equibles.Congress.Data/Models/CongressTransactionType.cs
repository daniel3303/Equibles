using System.ComponentModel.DataAnnotations;

namespace Equibles.Congress.Data.Models;

public enum CongressTransactionType {
    [Display(Name = "Purchase")] Purchase = 0,
    [Display(Name = "Sale")] Sale = 1
}
