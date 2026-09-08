using System.Linq.Expressions;
using Equibles.Sec.Data.Models;
using Equibles.Sec.FinancialFacts.Data.Models;

namespace Equibles.Sec.FinancialFacts.BusinessLogic;

internal static class CurrentShareEvidenceDates
{
    private static readonly DocumentType[] ReportingForms =
    [
        DocumentType.TenK,
        DocumentType.TenKa,
        DocumentType.TenQ,
        DocumentType.TenQa,
        DocumentType.TwentyF,
        DocumentType.TwentyFa,
        DocumentType.FortyF,
        DocumentType.FortyFa,
    ];

    // A current count cannot predate its own report's period or postdate its disclosure.
    // Keep the source fact intact; an invalid context never authorizes a guessed date repair.
    internal static readonly Expression<Func<FinancialFact, bool>> Eligible = fact =>
        fact.PeriodEnd <= fact.FiledDate
        && (
            fact.Document == null
            || !ReportingForms.Contains(fact.Document.DocumentType)
            || fact.PeriodEnd >= fact.Document.ReportingForDate
        );

    internal static readonly Func<FinancialFact, bool> IsEligible = Eligible.Compile();
}
