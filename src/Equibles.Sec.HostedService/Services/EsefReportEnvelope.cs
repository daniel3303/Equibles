using System.Text;
using Equibles.Core.Identity;
using Equibles.Sec.FinancialFacts.BusinessLogic;
using Equibles.Sec.FinancialFacts.BusinessLogic.Parsers;

namespace Equibles.Sec.HostedService.Services;

public static class EsefReportEnvelope
{
    public const int MaximumSourceBytes = 300 * 1024 * 1024;
    public const int MaximumEnvelopeBytes = 50 * 1024 * 1024;

    // Validate identity with Discover first; rendering must keep source names for CSS selectors.
    public static byte[] ForRetrieval(byte[] source) =>
        EsefEnvelopeWriter.Compact(source, requireEsef: true, canonicalize: false);

    public static byte[] Build(byte[] source, string lei, DateOnly periodEnd)
    {
        var envelope = EsefEnvelopeWriter.Compact(source);
        if (!EsefAnnualPeriod.IsProvenInline(Encoding.UTF8.GetString(envelope), lei, periodEnd))
            throw new InvalidDataException(
                "Issuer report does not prove the registered issuer and annual period."
            );
        return envelope;
    }

    public sealed record DiscoveredReport(
        string LegalEntityIdentifier,
        DateOnly PeriodEnd,
        byte[] Envelope
    );

    public static DiscoveredReport Discover(
        byte[] source,
        DateOnly publicationDate,
        DateOnly? annualPeriodStart = null
    )
    {
        if (publicationDate == default)
            throw new InvalidDataException("Report requires a publication date.");
        var envelope = EsefEnvelopeWriter.Compact(source, requireEsef: true);
        var facts = new InlineXbrlParser().Parse(Encoding.UTF8.GetString(envelope));
        var consolidated = facts
            .Where(fact => fact.Dimensions.Count == 0 && fact.ConsolidatedLei != null)
            .ToList();
        var identifiers = consolidated
            .Select(fact => fact.ConsolidatedLei)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (identifiers.Length != 1 || !InternationalSecurityIdentifiers.IsValidLei(identifiers[0]))
            throw new InvalidDataException(
                "Report requires exactly one valid consolidated issuer LEI."
            );
        var supported = consolidated
            .Where(fact =>
                fact.Taxonomy == "ifrs-full"
                && Uri.TryCreate(fact.Namespace, UriKind.Absolute, out var address)
                && address.Host == "xbrl.ifrs.org"
            )
            .ToList();
        var instantEnds = supported
            .Where(fact => fact.IsInstant)
            .Select(fact => fact.PeriodEnd)
            .ToHashSet();
        var annual = supported
            .Where(fact =>
                !fact.IsInstant
                && fact.PeriodEnd.DayNumber - fact.PeriodStart.DayNumber is >= 350 and <= 380
                && instantEnds.Contains(fact.PeriodEnd)
            )
            .ToArray();
        if (annual.Length == 0)
            throw new InvalidDataException(
                "Report does not prove an annual period and matching instant."
            );
        var periodEnd = annual.Max(fact => fact.PeriodEnd);
        if (periodEnd > publicationDate || supported.Any(fact => fact.PeriodEnd > periodEnd))
            throw new InvalidDataException(
                "Report has a future or later unsupported annual period."
            );
        if (
            !EsefAnnualPeriod.IsProvenInline(
                Encoding.UTF8.GetString(envelope),
                identifiers[0],
                periodEnd
            )
        )
            throw new InvalidDataException(
                "Report does not prove an unambiguous annual reporting period."
            );
        if (
            annualPeriodStart.HasValue
            && annual.Any(fact =>
                fact.PeriodEnd == periodEnd && fact.PeriodStart != annualPeriodStart.Value
            )
        )
            throw new InvalidDataException(
                "Annual report start date does not match its official catalogue."
            );
        return new(identifiers[0], periodEnd, envelope);
    }
}
