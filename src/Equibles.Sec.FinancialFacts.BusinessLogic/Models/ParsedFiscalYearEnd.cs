namespace Equibles.Sec.FinancialFacts.BusinessLogic.Models;

/// <summary>A fiscal year end explicitly stated for a consolidated filing context.</summary>
public record ParsedFiscalYearEnd(
    string Cik,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    int Month,
    int Day
);
