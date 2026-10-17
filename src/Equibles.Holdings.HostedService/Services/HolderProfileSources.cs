using Equibles.CommonStocks.Data.Helpers;
using Equibles.Holdings.BusinessLogic;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.HostedService.Models;
using Equibles.Sec.Data.Models;

namespace Equibles.Holdings.HostedService.Services;

/// <summary>
/// Every regulatory record a holder can be matched to, indexed by the filed identifiers holders
/// carry. Built once per refresh so each holder's evidence is a handful of dictionary reads.
/// </summary>
internal sealed class HolderProfileSources
{
    internal Dictionary<int, FormAdvAdviser> AdvisersByCrd { get; } = [];
    internal Dictionary<string, FormAdvAdviser> AdvisersBySecNumber { get; } =
        new(StringComparer.Ordinal);
    internal Dictionary<string, FormAdvAdviser> AdvisersByCik { get; } =
        new(StringComparer.Ordinal);
    internal Dictionary<int, List<FormAdvAdviserClientType>> ClientTypesByCrd { get; } = [];
    internal Dictionary<string, SecFilerProfile> FilerProfilesByCik { get; } =
        new(StringComparer.Ordinal);
    internal Dictionary<
        string,
        List<HolderClassificationEvidence>
    > BeneficialOwnershipByCik { get; } = new(StringComparer.Ordinal);
    internal Dictionary<string, NCenRegistrantRow> RegistrantsByCik { get; } =
        new(StringComparer.Ordinal);
    internal Dictionary<int, AdvisedSeriesSummary> AdvisedSeriesByCrd { get; } = [];

    internal static HolderProfileSources Build(
        IEnumerable<FormAdvAdviser> advisers,
        IEnumerable<FormAdvAdviserClientType> clientTypes,
        IEnumerable<SecFilerProfile> filerProfiles,
        IEnumerable<BeneficialOwnershipEvidenceRow> beneficialOwnership,
        IEnumerable<NCenRegistrantRow> registrants,
        IEnumerable<AdvisedSeriesRow> advisedSeries
    )
    {
        var sources = new HolderProfileSources();
        var adviserList = advisers.Where(adviser => adviser.Registration != null).ToList();
        foreach (var adviser in adviserList)
            sources.AdvisersByCrd[adviser.Crd] = adviser;

        // A SEC file number or a CIK identifies an adviser only when exactly one roster row carries it.
        foreach (
            var group in adviserList
                .Select(adviser =>
                    (Key: HolderIdentifierRules.NormalizeSecFileNumber(adviser.SecNumber), adviser)
                )
                .Where(item => item.Key != null)
                .GroupBy(item => item.Key)
                .Where(group => group.Count() == 1)
        )
            sources.AdvisersBySecNumber[group.Key] = group.Single().adviser;
        foreach (
            var group in adviserList
                .Select(adviser => (Key: CikNormalizer.Canonicalize(adviser.Cik), adviser))
                .Where(item => item.Key != null)
                .GroupBy(item => item.Key)
                .Where(group => group.Count() == 1)
        )
            sources.AdvisersByCik[group.Key] = group.Single().adviser;

        // Only the client types filed with the adviser's current roster row describe it now.
        foreach (var clientType in clientTypes)
        {
            if (
                !sources.AdvisersByCrd.TryGetValue(clientType.Crd, out var adviser)
                || adviser.ReportDate != clientType.ReportDate
            )
                continue;
            if (!sources.ClientTypesByCrd.TryGetValue(clientType.Crd, out var list))
                sources.ClientTypesByCrd[clientType.Crd] = list = [];
            list.Add(clientType);
        }

        foreach (var profile in filerProfiles.Where(profile => profile.Found))
        {
            var cik = CikNormalizer.Canonicalize(profile.Cik);
            if (cik != null)
                sources.FilerProfilesByCik[cik] = profile;
        }

        foreach (var filing in beneficialOwnership)
        foreach (var (cik, fact) in HolderIdentifierRules.AttributeBeneficialOwnership(filing))
        {
            if (!sources.BeneficialOwnershipByCik.TryGetValue(cik, out var list))
                sources.BeneficialOwnershipByCik[cik] = list = [];
            list.Add(fact);
        }

        foreach (
            var registrant in registrants
                .Where(row => CikNormalizer.Canonicalize(row.Cik) != null)
                .GroupBy(row => CikNormalizer.Canonicalize(row.Cik))
                .Select(group =>
                    group
                        .OrderByDescending(row => row.ReportEndingPeriod)
                        .ThenByDescending(row => row.AccessionNumber, StringComparer.Ordinal)
                        .First()
                )
        )
            sources.RegistrantsByCik[CikNormalizer.Canonicalize(registrant.Cik)] = registrant;

        foreach (var summary in HolderIdentifierRules.SummarizeAdvisedSeries(advisedSeries))
            sources.AdvisedSeriesByCrd[summary.Key] = summary.Value;

        return sources;
    }
}
