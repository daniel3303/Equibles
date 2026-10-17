using Equibles.CommonStocks.Data.Helpers;
using Equibles.Holdings.BusinessLogic;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.HostedService.Models;
using Equibles.Sec.Data.Models;

namespace Equibles.Holdings.HostedService.Services;

/// <summary>
/// How filed identifiers are compared and which 13D/13G facts speak for a holder. Every match is
/// on a CIK, CRD or SEC file number; names are never compared.
/// </summary>
internal static class HolderIdentifierRules
{
    /// <summary>A CRD as filed ("000160108") as the roster's integer, or null when it is not a number.</summary>
    internal static int? ParseCrd(string crd)
    {
        var digits = crd?.Trim().TrimStart('0');
        return
            !string.IsNullOrEmpty(digits)
            && digits.All(char.IsAsciiDigit)
            && digits.Length <= 9
            && int.TryParse(digits, out var value)
            && value > 0
            ? value
            : null;
    }

    /// <summary>
    /// An adviser's SEC file number ("801-117266", "802-01234") with both parts' leading zeros
    /// dropped, or null for any other prefix.
    /// </summary>
    internal static string NormalizeSecFileNumber(string fileNumber)
    {
        var parts = fileNumber?.Trim().Split('-');
        if (
            parts is not { Length: 2 }
            || !int.TryParse(parts[0], out var prefix)
            || !int.TryParse(parts[1], out var number)
            || number <= 0
            || prefix is not (801 or 802)
        )
            return null;
        return $"{prefix}-{number}";
    }

    /// <summary>
    /// The 13D/13G facts a filing states about identifiable holders: a 13D person by its own CIK,
    /// and a 13G (whose persons carry no CIK) only when it names one person, who is then the filer.
    /// </summary>
    internal static IEnumerable<(
        string Cik,
        HolderClassificationEvidence Fact
    )> AttributeBeneficialOwnership(BeneficialOwnershipEvidenceRow filing)
    {
        if (filing.FilingType == FilingType.Schedule13D)
        {
            foreach (var person in filing.Persons)
            {
                var cik = CikNormalizer.Canonicalize(person.Cik);
                if (cik != null && person.Types != ReportingPersonTypes.None)
                    yield return (
                        cik,
                        Fact(
                            filing,
                            HolderEvidenceSource.Schedule13D,
                            HolderEvidenceFields.TypeOfReportingPerson,
                            person.Types
                        )
                    );
            }
            yield break;
        }

        var filer = CikNormalizer.Canonicalize(filing.FilerCik);
        if (
            filing.FilingType != FilingType.Schedule13G
            || filer == null
            || filing.Persons.Count != 1
        )
            yield break;
        if (filing.Persons[0].Types != ReportingPersonTypes.None)
            yield return (
                filer,
                Fact(
                    filing,
                    HolderEvidenceSource.Schedule13G,
                    HolderEvidenceFields.TypeOfReportingPerson,
                    filing.Persons[0].Types
                )
            );
        if (filing.FilingPersonTypes != ReportingPersonTypes.None)
            yield return (
                filer,
                Fact(
                    filing,
                    HolderEvidenceSource.Schedule13G,
                    HolderEvidenceFields.TypeOfPersonFiling,
                    filing.FilingPersonTypes
                )
            );
    }

    /// <summary>
    /// Each adviser's registered fund series as primary adviser by the series' latest report, so a
    /// series that changed adviser counts only for its current one.
    /// </summary>
    internal static Dictionary<int, AdvisedSeriesSummary> SummarizeAdvisedSeries(
        IEnumerable<AdvisedSeriesRow> rows
    ) =>
        rows.GroupBy(row => row.SeriesKey, StringComparer.Ordinal)
            .SelectMany(series =>
            {
                // Every primary adviser of the latest report counts the series once; one the
                // report names without a CRD is unknown, never the previous adviser.
                var latest = series.Max(row => row.ReportEndingPeriod);
                return series
                    .Where(row => row.ReportEndingPeriod == latest && row.Crd != null)
                    .DistinctBy(row => row.Crd);
            })
            .GroupBy(row => row.Crd.GetValueOrDefault())
            .ToDictionary(
                group => group.Key,
                group =>
                {
                    var current = group.ToList();
                    var index = current.Where(row => IsIndex(row.FundTypes)).ToList();
                    return new AdvisedSeriesSummary(
                        current.Count,
                        index.Count,
                        current.Count(row => IsEtf(row.FundTypes)),
                        Sum(current),
                        Sum(index),
                        current.Max(row => row.ReportEndingPeriod)
                    );
                }
            );

    private static bool IsIndex(NCenFundTypes types) =>
        (types & (NCenFundTypes.IndexFund | NCenFundTypes.MultipleOrInverseIndexFund)) != 0;

    private static bool IsEtf(NCenFundTypes types) =>
        (types & (NCenFundTypes.ExchangeTradedFund | NCenFundTypes.ExchangeTradedManagedFund)) != 0;

    // The sum of the reported figures; null only when no series reports one.
    private static decimal? Sum(IReadOnlyCollection<AdvisedSeriesRow> rows) =>
        rows.Any(row => row.NetAssets != null) ? rows.Sum(row => row.NetAssets ?? 0) : null;

    private static HolderClassificationEvidence Fact(
        BeneficialOwnershipEvidenceRow filing,
        HolderEvidenceSource source,
        string field,
        ReportingPersonTypes types
    ) =>
        new()
        {
            Source = source,
            MatchedOn = HolderEvidenceMatch.Cik,
            Field = field,
            Value = types.ToString(),
            SourceKey = filing.AccessionNumber,
            AsOf = filing.FilingDate,
        };
}
