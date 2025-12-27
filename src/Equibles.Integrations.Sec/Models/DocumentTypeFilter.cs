using System.ComponentModel.DataAnnotations;

namespace Equibles.Integrations.Sec.Models;

public enum DocumentTypeFilter {
    [Display(Name = "10-K")] TenK,
    [Display(Name = "10-Q")] TenQ,
    [Display(Name = "10-K/A")] TenKa,
    [Display(Name = "10-Q/A")] TenQa,
    [Display(Name = "8-K")] EightK,
    [Display(Name = "8-K/A")] EightKa,
    [Display(Name = "20-F")] TwentyF,
    [Display(Name = "6-K")] SixK,
    [Display(Name = "40-F")] FortyF,
    [Display(Name = "4")] FormFour,
    [Display(Name = "3")] FormThree
}