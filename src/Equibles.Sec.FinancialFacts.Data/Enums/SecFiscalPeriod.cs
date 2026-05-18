using System.ComponentModel.DataAnnotations;

namespace Equibles.Sec.FinancialFacts.Data.Enums;

/// <summary>
/// SEC fiscal-period qualifier as reported in the Company Facts API <c>fp</c>
/// field: <c>FY</c> (annual) or <c>Q1</c>–<c>Q4</c>. Distinct from
/// <c>Equibles.CommonStocks.BusinessLogic.FiscalPeriod</c>, which is a
/// (Year, Quarter) value object for date math and has no annual marker.
/// </summary>
public enum SecFiscalPeriod
{
    [Display(Name = "FY")]
    FullYear,

    [Display(Name = "Q1")]
    Q1,

    [Display(Name = "Q2")]
    Q2,

    [Display(Name = "Q3")]
    Q3,

    [Display(Name = "Q4")]
    Q4,
}
