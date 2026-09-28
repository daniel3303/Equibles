using Equibles.Sec.FinancialFacts.BusinessLogic.Models;
using Equibles.Sec.FinancialFacts.BusinessLogic.Parsers;

namespace Equibles.Sec.FinancialFacts.BusinessLogic;

public static class EsefAnnualPeriod
{
    public static bool IsProvenInline(string html, string issuerLei, DateOnly periodEnd) =>
        EsefInlineXbrlParser.TryParse(html, out var facts) && IsProven(facts, issuerLei, periodEnd);

    public static bool IsProven(
        IEnumerable<ParsedXbrlFact> facts,
        string issuerLei,
        DateOnly periodEnd
    )
    {
        if (string.IsNullOrEmpty(issuerLei))
            return false;

        var matching = facts
            .Where(fact =>
                string.Equals(fact.ConsolidatedLei, issuerLei, StringComparison.OrdinalIgnoreCase)
                && fact.Dimensions.Count == 0
                && !string.IsNullOrEmpty(fact.Unit)
                && Uri.TryCreate(fact.Namespace, UriKind.Absolute, out var address)
                && address.Scheme is "http" or "https"
                && address.Host == "xbrl.ifrs.org"
                && address.AbsolutePath.EndsWith("/ifrs-full", StringComparison.Ordinal)
            )
            .ToList();
        if (matching.Any(fact => fact.PeriodEnd > periodEnd))
            return false;

        var current = matching
            .Where(fact => fact.PeriodEnd == periodEnd)
            .GroupBy(fact => (fact.Tag, fact.Unit, fact.PeriodStart, fact.IsInstant))
            .Where(group => group.Select(fact => fact.Value).Distinct().Take(2).Count() == 1)
            .Select(group => group.First())
            .ToList();
        return current.Any(fact => fact.IsInstant)
            && current.Any(fact =>
                !fact.IsInstant
                && fact.PeriodEnd.DayNumber - fact.PeriodStart.DayNumber is >= 350 and <= 380
            );
    }
}
