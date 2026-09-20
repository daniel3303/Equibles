using System.Globalization;
using Equibles.Core.Identity;
using Equibles.Integrations.XbrlFilings.Models;

namespace Equibles.Integrations.XbrlFilings;

// One issuer-period can hold several filings: the same annual report is filed in more than one country, and a
// country can file a correction beside its first. This picks exactly one, by a total rule, so the same corpus
// always yields the same choice and one period's facts are never counted twice.
public static class EsefFilingSelection
{
    // Every part the filing's own key is built from, the country included: one issuer-period holds several
    // countries' filings, so a row that states none cannot be told apart from its siblings and is not a
    // filing this lane can choose.
    public static bool IsEsefWithLegalEntityIdentifier(XbrlFiling filing) =>
        filing != null
        && filing.Regime == XbrlFilingsParser.EsefRegime
        && InternationalSecurityIdentifiers.IsValidLei(filing.EntityIdentifier)
        && filing.PeriodEnd != null
        && filing.ReportUrl != null
        && filing.CountryCode is { Length: 2 };

    // The issuer's own market decides first: a Paris-listed issuer's French filing is the one its home
    // regulator received. A clean validation beats a flagged one, then the earliest addition, then the country
    // code, so nothing is left to the order the host happened to serve.
    public static XbrlFiling PickForPeriod(
        IEnumerable<XbrlFiling> filings,
        string preferredCountryCode
    ) =>
        filings
            ?.Where(IsEsefWithLegalEntityIdentifier)
            .OrderByDescending(filing =>
                preferredCountryCode != null
                && string.Equals(
                    filing.CountryCode,
                    preferredCountryCode,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            .ThenBy(filing => filing.ErrorCount)
            .ThenBy(filing => filing.AddedAt ?? DateTime.MaxValue)
            .ThenBy(filing => filing.CountryCode, StringComparer.Ordinal)
            .ThenBy(filing => filing.FilingKey, StringComparer.Ordinal)
            .FirstOrDefault();

    // The latest annual report the issuer has filed, chosen period by period so a later period's flagged
    // filing never loses to an earlier period's clean one.
    public static XbrlFiling PickLatest(
        IEnumerable<XbrlFiling> filings,
        string preferredCountryCode
    )
    {
        var eligible = filings?.Where(IsEsefWithLegalEntityIdentifier).ToList();
        if (eligible == null || eligible.Count == 0)
            return null;
        var latest = eligible.Max(filing => filing.PeriodEnd);
        return PickForPeriod(
            eligible.Where(filing => filing.PeriodEnd == latest),
            preferredCountryCode
        );
    }

    public static IReadOnlyList<XbrlFiling> PickHistory(
        IEnumerable<XbrlFiling> filings,
        string preferredCountryCode
    ) =>
        filings
            ?.Where(IsEsefWithLegalEntityIdentifier)
            .GroupBy(filing => filing.PeriodEnd)
            .OrderByDescending(period => period.Key)
            .Select(period => PickForPeriod(period, preferredCountryCode))
            .ToList()
        ?? [];

    // The filing key the store uses. `AccessionNumber` holds 32 characters and this is exactly 32: a
    // twenty-character identifier, an eight-digit period and a two-letter country, with two separators.
    public const int FilingReferenceLength = 32;

    public static string FilingReference(XbrlFiling filing)
    {
        ArgumentNullException.ThrowIfNull(filing);
        if (filing.CountryCode is not { Length: 2 })
            throw new InvalidDataException(
                "A filing reference needs the filing's two-letter country."
            );
        if (!IsEsefWithLegalEntityIdentifier(filing))
            throw new InvalidDataException(
                "Only an ESEF filing with an LEI has a filing reference."
            );
        // The key is stored, so it is written in the invariant calendar: a Thai or Umm al-Qura host would
        // otherwise spell the same period differently and the lane would re-capture it every cycle.
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{filing.EntityIdentifier}-{filing.PeriodEnd:yyyyMMdd}-{filing.CountryCode.ToUpperInvariant()}"
        );
    }
}
