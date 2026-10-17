using Equibles.CommonStocks.Data.Helpers;
using Equibles.Holdings.BusinessLogic;
using Equibles.Holdings.BusinessLogic.Models;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.HostedService.Models;
using Equibles.Sec.Data.Models;

namespace Equibles.Holdings.HostedService.Services;

/// <summary>Collects one holder's regulatory evidence and derives its profile from it.</summary>
internal static class HolderProfileBuilder
{
    private static readonly IReadOnlyDictionary<FormAdvClientType, HolderClientBase> ClientBases =
        new Dictionary<FormAdvClientType, HolderClientBase>
        {
            [FormAdvClientType.Individuals] = HolderClientBase.Individuals,
            [FormAdvClientType.HighNetWorthIndividuals] = HolderClientBase.Individuals,
            [FormAdvClientType.BanksOrThrifts] = HolderClientBase.BanksAndThrifts,
            [FormAdvClientType.InvestmentCompanies] = HolderClientBase.RegisteredFunds,
            [FormAdvClientType.BusinessDevelopmentCompanies] = HolderClientBase.RegisteredFunds,
            [FormAdvClientType.PooledInvestmentVehicles] =
                HolderClientBase.PooledInvestmentVehicles,
            [FormAdvClientType.PensionAndProfitSharingPlans] = HolderClientBase.PensionPlans,
            [FormAdvClientType.CharitableOrganizations] = HolderClientBase.CharitableOrganizations,
            [FormAdvClientType.GovernmentEntities] = HolderClientBase.GovernmentAndSovereign,
            [FormAdvClientType.SovereignWealthFunds] = HolderClientBase.GovernmentAndSovereign,
            [FormAdvClientType.OtherInvestmentAdvisers] = HolderClientBase.OtherInvestmentAdvisers,
            [FormAdvClientType.InsuranceCompanies] = HolderClientBase.InsuranceCompanies,
            [FormAdvClientType.Corporations] = HolderClientBase.Corporations,
            [FormAdvClientType.Other] = HolderClientBase.Other,
        };

    internal static List<HolderClassificationEvidence> CollectEvidence(
        HolderIdentityRow holder,
        HolderProfileSources sources
    )
    {
        var facts = new List<HolderClassificationEvidence>();
        var cik = CikNormalizer.Canonicalize(holder.Cik);

        // Each linked adviser once, under the strongest identifier that links it.
        var advisers = new Dictionary<int, HolderEvidenceMatch>();
        void Link(FormAdvAdviser adviser, HolderEvidenceMatch match) =>
            advisers.TryAdd(adviser.Crd, match);
        if (
            HolderIdentifierRules.ParseCrd(holder.CrdNumber) is { } crd
            && sources.AdvisersByCrd.TryGetValue(crd, out var byCrd)
        )
            Link(byCrd, HolderEvidenceMatch.CoverCrd);
        if (
            HolderIdentifierRules.NormalizeSecFileNumber(holder.SecFileNumber) is { } fileNumber
            && sources.AdvisersBySecNumber.TryGetValue(fileNumber, out var byFileNumber)
        )
            Link(byFileNumber, HolderEvidenceMatch.CoverSecFileNumber);
        if (cik != null && sources.AdvisersByCik.TryGetValue(cik, out var byCik))
            Link(byCik, HolderEvidenceMatch.Cik);
        foreach (var (adviserCrd, match) in advisers)
        {
            var adviser = sources.AdvisersByCrd[adviserCrd];
            facts.Add(
                new HolderClassificationEvidence
                {
                    Source = HolderEvidenceSource.FormAdvRoster,
                    MatchedOn = match,
                    Field = HolderEvidenceFields.AdvRegistration,
                    Value = adviser.Registration.ToString(),
                    SourceKey = adviserCrd.ToString(),
                    AsOf = adviser.ReportDate,
                }
            );
        }

        if (cik != null)
        {
            if (
                sources.FilerProfilesByCik.TryGetValue(cik, out var filerProfile)
                && !string.IsNullOrWhiteSpace(filerProfile.Sic)
            )
                facts.Add(
                    new HolderClassificationEvidence
                    {
                        Source = HolderEvidenceSource.SecFilerProfile,
                        MatchedOn = HolderEvidenceMatch.Cik,
                        Field = HolderEvidenceFields.Sic,
                        Value = filerProfile.Sic.Trim(),
                        SourceKey = cik,
                        AsOf = DateOnly.FromDateTime(filerProfile.FetchedAt),
                    }
                );

            if (sources.BeneficialOwnershipByCik.TryGetValue(cik, out var selfReports))
                facts.AddRange(
                    selfReports.Select(fact => new HolderClassificationEvidence
                    {
                        Source = fact.Source,
                        MatchedOn = fact.MatchedOn,
                        Field = fact.Field,
                        Value = fact.Value,
                        SourceKey = fact.SourceKey,
                        AsOf = fact.AsOf,
                    })
                );

            if (
                sources.RegistrantsByCik.TryGetValue(cik, out var registrant)
                && !string.IsNullOrWhiteSpace(registrant.InvestmentCompanyType)
            )
                facts.Add(
                    new HolderClassificationEvidence
                    {
                        Source = HolderEvidenceSource.NCenRegistrant,
                        MatchedOn = HolderEvidenceMatch.Cik,
                        Field = HolderEvidenceFields.InvestmentCompanyType,
                        Value = registrant.InvestmentCompanyType.Trim(),
                        SourceKey = registrant.AccessionNumber,
                        AsOf = registrant.ReportEndingPeriod,
                    }
                );
        }

        foreach (var fact in facts)
            fact.InstitutionalHolderId = holder.Id;
        return facts;
    }

    internal static InstitutionalHolderProfile Build(
        HolderIdentityRow holder,
        IReadOnlyCollection<HolderClassificationEvidence> evidence,
        HolderProfileSources sources,
        HolderEvidenceContext context
    )
    {
        var decision = HolderEntityTypeDeriver.Derive(evidence, context);
        var profile = new InstitutionalHolderProfile
        {
            InstitutionalHolderId = holder.Id,
            EntityType = decision.EntityType,
            EntityTypeSource = decision.Source,
            EntityTypeSourceKey = decision.SourceKey,
            EntityTypeAsOf = decision.AsOf,
            HasConflictingEvidence = decision.HasConflictingEvidence,
            DerivationVersion = HolderEntityTypeDeriver.Version,
        };

        // The adviser behind the profile is the strongest current Form ADV link; a former
        // listing describes nothing about the holder today.
        var adviserFact = evidence
            .Where(fact =>
                fact.Source == HolderEvidenceSource.FormAdvRoster
                && HolderEntityTypeDeriver.IsCurrent(fact, context)
            )
            .OrderBy(fact => fact.MatchedOn)
            .ThenByDescending(fact => fact.AsOf)
            .FirstOrDefault();
        if (
            adviserFact != null
            && sources.AdvisersByCrd.TryGetValue(int.Parse(adviserFact.SourceKey), out var adviser)
        )
        {
            profile.AdviserCrd = adviser.Crd;
            profile.AdviserAsOf = adviser.ReportDate;
            profile.RegulatoryAum = adviser.TotalRegulatoryAum;
            profile.Lei = adviser.Lei;
            profile.AdvisesPrivateFunds = adviser.AdvisesPrivateFunds;
            profile.PrivateFundCount = adviser.PrivateFundCount;
            profile.PrivateFundGrossAssets = adviser.PrivateFundGrossAssets;
            profile.PrivateFundFocus = Focus(adviser);
            (profile.ClientBase, profile.ClientBaseAumShare, profile.PooledVehicleAumShare) =
                ClientBase(sources.ClientTypesByCrd.GetValueOrDefault(adviser.Crd) ?? []);
        }

        var seriesCrd = profile.AdviserCrd ?? HolderIdentifierRules.ParseCrd(holder.CrdNumber);
        if (
            seriesCrd != null
            && sources.AdvisedSeriesByCrd.TryGetValue(seriesCrd.Value, out var series)
        )
        {
            profile.AdvisedSeriesCount = series.SeriesCount;
            profile.IndexSeriesCount = series.IndexSeriesCount;
            profile.EtfSeriesCount = series.EtfSeriesCount;
            profile.AdvisedNetAssets = series.NetAssets;
            profile.IndexNetAssets = series.IndexNetAssets;
            profile.AdvisedSeriesAsOf = series.AsOf;
        }

        var cik = CikNormalizer.Canonicalize(holder.Cik);
        if (cik != null && sources.FilerProfilesByCik.TryGetValue(cik, out var filerProfile))
            profile.Sic = filerProfile.Sic?.Trim();
        return profile;
    }

    /// <summary>
    /// The client group holding at least half of the regulatory assets, else Diversified; Unknown
    /// when no assets are reported (an exempt reporting adviser files no Item 5).
    /// </summary>
    internal static (HolderClientBase Base, decimal? Share, decimal? PooledShare) ClientBase(
        IReadOnlyCollection<FormAdvAdviserClientType> clientTypes
    )
    {
        var total = clientTypes.Sum(row => row.RegulatoryAum ?? 0);
        if (total <= 0)
            return (HolderClientBase.Unknown, null, null);

        var shares = clientTypes
            .Where(row => ClientBases.ContainsKey(row.ClientType))
            .GroupBy(row => ClientBases[row.ClientType])
            .ToDictionary(
                group => group.Key,
                group => (decimal)group.Sum(row => row.RegulatoryAum ?? 0) / total
            );
        var pooled = shares.GetValueOrDefault(HolderClientBase.PooledInvestmentVehicles);
        var majority = shares.Where(pair => pair.Value >= 0.5m).ToList();
        return majority.Count == 1
            ? (majority[0].Key, majority[0].Value, pooled)
            : (HolderClientBase.Diversified, null, pooled);
    }

    /// <summary>The private fund type that is more than half of the adviser's private funds by count.</summary>
    internal static PrivateFundFocus Focus(FormAdvAdviser adviser)
    {
        (PrivateFundFocus Focus, int Count)[] counts =
        [
            (PrivateFundFocus.Hedge, adviser.HedgeFundCount ?? 0),
            (PrivateFundFocus.PrivateEquity, adviser.PrivateEquityFundCount ?? 0),
            (PrivateFundFocus.VentureCapital, adviser.VentureCapitalFundCount ?? 0),
            (PrivateFundFocus.RealEstate, adviser.RealEstateFundCount ?? 0),
            (PrivateFundFocus.Liquidity, adviser.LiquidityFundCount ?? 0),
            (PrivateFundFocus.Securitized, adviser.SecuritizedAssetFundCount ?? 0),
            (PrivateFundFocus.Other, adviser.OtherPrivateFundCount ?? 0),
        ];
        var total = counts.Sum(item => item.Count);
        if (total <= 0)
            return PrivateFundFocus.None;
        var majority = counts.FirstOrDefault(item => item.Count * 2 > total);
        return majority.Count > 0 ? majority.Focus : PrivateFundFocus.Mixed;
    }
}
