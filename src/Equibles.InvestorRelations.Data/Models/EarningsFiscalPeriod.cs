using System.ComponentModel.DataAnnotations;

namespace Equibles.InvestorRelations.Data.Models;

public enum EarningsFiscalPeriod
{
    [Display(Name = "Unknown")]
    Unknown,

    [Display(Name = "Q1")]
    Q1,

    [Display(Name = "Q2")]
    Q2,

    [Display(Name = "Q3")]
    Q3,

    [Display(Name = "Q4")]
    Q4,

    [Display(Name = "Full Year")]
    FullYear,
}
