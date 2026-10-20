using Equibles.CommonStocks.Data.Helpers;
using Equibles.Holdings.BusinessLogic;
using Equibles.Holdings.BusinessLogic.Advisers;
using Equibles.Holdings.BusinessLogic.Models;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.HostedService.Models;
using Equibles.Sec.Data.Models;

namespace Equibles.Holdings.HostedService.Services;

/// <summary>Collects one holder's regulatory evidence and derives its profile from it.</summary>
internal static class HolderProfileBuilder
{
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
            if (adviser.AdvisesPrivateFunds != false)
                (
                    profile.PrivateFundFocus,
                    profile.PrivateFundFocusBasis,
                    profile.PrivateFundFocusAsOf
                ) = AdviserPrivateFundFocus.Decide(
                    RosterFundCounts.Of(adviser),
                    adviser.ReportDate,
                    sources.PrivateFundFilingsByCrd.GetValueOrDefault(adviser.Crd),
                    context.Today
                );
            (profile.ClientBase, profile.ClientBaseAumShare, profile.PooledVehicleAumShare) =
                AdviserClientBase.Of(
                    (sources.ClientTypesByCrd.GetValueOrDefault(adviser.Crd) ?? []).Select(row =>
                        (row.ClientType, row.RegulatoryAum)
                    )
                );
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
}
