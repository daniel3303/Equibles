using System.ComponentModel.DataAnnotations;

namespace Equibles.Sec.FinancialFacts.Data.Enums;

/// <summary>
/// Whether a fact covers a span of time (income statement / cash flow) or a
/// single point in time (balance sheet). SEC's Company Facts API expresses
/// duration facts with <c>start</c>+<c>end</c> and instant facts with
/// <c>end</c> only.
/// </summary>
public enum FactPeriodType
{
    [Display(Name = "Duration")]
    Duration,

    [Display(Name = "Instant")]
    Instant,
}
