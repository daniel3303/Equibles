using System.ComponentModel.DataAnnotations;

namespace Equibles.InsiderTrading.Data.Models;

public enum TransactionCode {
    [Display(Name = "Purchase")] Purchase,
    [Display(Name = "Sale")] Sale,
    [Display(Name = "Award")] Award,
    [Display(Name = "Conversion")] Conversion,
    [Display(Name = "Exercise")] Exercise,
    [Display(Name = "Tax Payment")] TaxPayment,
    [Display(Name = "Expiration")] Expiration,
    [Display(Name = "Gift")] Gift,
    [Display(Name = "Inheritance")] Inheritance,
    [Display(Name = "Discretionary")] Discretionary,
    [Display(Name = "Other")] Other
}
