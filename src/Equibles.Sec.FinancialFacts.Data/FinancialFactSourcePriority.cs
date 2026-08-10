using Equibles.Sec.Data.Models;

namespace Equibles.Sec.FinancialFacts.Data;

/// <summary>
/// Ranks the source form for two facts that describe the same concept and actual period.
/// Periodic reports carry the audited/reviewed statement value; a later proxy can repeat the
/// same figure rounded for pay-versus-performance disclosure and must not restate it.
/// </summary>
public static class FinancialFactSourcePriority
{
    public static int Rank(DocumentType form)
    {
        if (
            form == DocumentType.TenK
            || form == DocumentType.TenKa
            || form == DocumentType.TenQ
            || form == DocumentType.TenQa
            || form == DocumentType.TwentyF
            || form == DocumentType.FortyF
        )
            return 0;

        if (
            form == DocumentType.EightK
            || form == DocumentType.EightKa
            || form == DocumentType.SixK
        )
            return 1;

        return 2;
    }
}
