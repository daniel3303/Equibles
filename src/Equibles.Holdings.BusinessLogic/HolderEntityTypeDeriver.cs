using Equibles.Holdings.BusinessLogic.Models;
using Equibles.Holdings.Data.Models;

namespace Equibles.Holdings.BusinessLogic;

/// <summary>
/// Decides a holder's entity type from its stored regulatory evidence, strongest source first:
/// its own Form ADV listing, its SEC SIC code, its recent 13D/13G self-report, then an N-CEN
/// registrant report. No current evidence means Unknown; nothing is ever read from a name.
/// A parent holding company (13D/13G code HC) is the weakest evidence: any current operating
/// type, from any source, decides instead.
/// </summary>
public static class HolderEntityTypeDeriver
{
    /// <summary>
    /// Stamped on every profile; bump when the rules change so readers can tell them apart.
    /// 2: HC reads HoldingCompany, and a multi-person 13G filed only under Rule 13d-1(b) with
    /// one Item 3 code speaks for its filer. 3: the private-fund focus weighs gross assets from a
    /// Schedule D 7.B.(1) filing at most 24 months old before roster fund counts, and quarter
    /// readers take the client base from the Form ADV roster current at each quarter end.
    /// </summary>
    public const int Version = 3;

    public const int BeneficialOwnershipLookbackMonths = 36;
    public const int NCenLookbackMonths = 24;

    // SIC codes that name an institution type on their own. 6211 (security brokers and dealers)
    // is left out because bank and insurance holding companies file under it, and 6411 because it
    // is insurance agents and brokers, not insurers.
    private static readonly IReadOnlyDictionary<string, HolderEntityType> SicTypes = new Dictionary<
        string,
        HolderEntityType
    >
    {
        ["6021"] = HolderEntityType.Bank,
        ["6022"] = HolderEntityType.Bank,
        ["6029"] = HolderEntityType.Bank,
        ["6035"] = HolderEntityType.Bank,
        ["6036"] = HolderEntityType.Bank,
        ["6712"] = HolderEntityType.Bank,
        ["6311"] = HolderEntityType.InsuranceCompany,
        ["6321"] = HolderEntityType.InsuranceCompany,
        ["6324"] = HolderEntityType.InsuranceCompany,
        ["6331"] = HolderEntityType.InsuranceCompany,
        ["6351"] = HolderEntityType.InsuranceCompany,
        ["6361"] = HolderEntityType.InsuranceCompany,
        ["6399"] = HolderEntityType.InsuranceCompany,
        ["6722"] = HolderEntityType.RegisteredFund,
        ["6726"] = HolderEntityType.RegisteredFund,
        ["6282"] = HolderEntityType.InvestmentAdviser,
    };

    // A person stating several mappable codes (a broker-dealer that is also an adviser) takes
    // the first in this order, and the profile is flagged. HC comes last: a parent that is also
    // an adviser or a bank reads as that, and the pair is not a conflict.
    private static readonly (ReportingPersonTypes Codes, HolderEntityType Type)[] PersonTypes =
    [
        (
            ReportingPersonTypes.Bank | ReportingPersonTypes.SavingsAssociation,
            HolderEntityType.Bank
        ),
        (ReportingPersonTypes.InsuranceCompany, HolderEntityType.InsuranceCompany),
        (ReportingPersonTypes.InvestmentCompany, HolderEntityType.RegisteredFund),
        (ReportingPersonTypes.BrokerDealer, HolderEntityType.BrokerDealer),
        (ReportingPersonTypes.InvestmentAdviser, HolderEntityType.InvestmentAdviser),
        (
            ReportingPersonTypes.EmployeeBenefitPlan | ReportingPersonTypes.ChurchPlan,
            HolderEntityType.BenefitPlan
        ),
        (ReportingPersonTypes.NonUsInstitution, HolderEntityType.NonUsInstitution),
        (ReportingPersonTypes.ParentHoldingCompany, HolderEntityType.HoldingCompany),
    ];

    private static readonly HolderEvidenceSource[] Precedence =
    [
        HolderEvidenceSource.FormAdvRoster,
        HolderEvidenceSource.SecFilerProfile,
        HolderEvidenceSource.Schedule13D,
        HolderEvidenceSource.NCenRegistrant,
    ];

    public static HolderEntityTypeDecision Derive(
        IEnumerable<HolderClassificationEvidence> evidence,
        HolderEvidenceContext context
    )
    {
        var current = evidence
            .Where(fact => IsCurrent(fact, context))
            .Select(fact => (Fact: fact, Types: ImpliedTypes(fact)))
            .Where(item => item.Types.Count > 0)
            .ToList();
        if (current.Count == 0)
            return HolderEntityTypeDecision.Unknown;

        // Two current Form ADV listings tied to one holder disagree even when both are registered.
        var conflicting =
            HasConflict(current.SelectMany(item => item.Types))
            || current
                .Where(item => item.Fact.Source == HolderEvidenceSource.FormAdvRoster)
                .Select(item => item.Fact.SourceKey)
                .Distinct()
                .Count() > 1;
        // A fact implying only HoldingCompany decides only when no current fact of any source
        // implies an operating type: not an older 13D/13G, not the other half of the same filing
        // (an HC cover page beside an FI Item 3), not a lower-ranked N-CEN registrant report.
        var operating = current.Where(item => !IsHoldingCompanyOnly(item.Types)).ToList();
        var candidates = operating.Count > 0 ? operating : current;
        var decisive = Precedence
            .Select(source =>
                candidates
                    .Where(item => Rank(item.Fact.Source) == source)
                    .OrderBy(item => item.Fact.MatchedOn)
                    .ThenByDescending(item => item.Fact.AsOf)
                    .ThenByDescending(item => item.Fact.SourceKey, StringComparer.Ordinal)
                    .FirstOrDefault()
            )
            .FirstOrDefault(item => item.Fact != null);
        if (decisive.Fact == null)
            return HolderEntityTypeDecision.Unknown;

        return new HolderEntityTypeDecision(
            decisive.Types[0],
            decisive.Fact.Source,
            decisive.Fact.SourceKey,
            decisive.Fact.AsOf,
            conflicting
        );
    }

    /// <summary>The types a fact implies, strongest first; empty when it implies none.</summary>
    internal static IReadOnlyList<HolderEntityType> ImpliedTypes(HolderClassificationEvidence fact)
    {
        switch (fact.Source)
        {
            case HolderEvidenceSource.FormAdvRoster
                when fact.Field == HolderEvidenceFields.AdvRegistration:
                return fact.Value switch
                {
                    HolderEvidenceFields.AdvRegistered => [HolderEntityType.RegisteredAdviser],
                    HolderEvidenceFields.AdvExemptReporting =>
                    [
                        HolderEntityType.ExemptReportingAdviser,
                    ],
                    _ => [],
                };
            case HolderEvidenceSource.SecFilerProfile when fact.Field == HolderEvidenceFields.Sic:
                return fact.Value != null && SicTypes.TryGetValue(fact.Value.Trim(), out var type)
                    ? [type]
                    : [];
            case HolderEvidenceSource.Schedule13D or HolderEvidenceSource.Schedule13G:
                if (!Enum.TryParse<ReportingPersonTypes>(fact.Value, out var codes))
                    return [];
                return PersonTypes
                    .Where(mapping => (codes & mapping.Codes) != 0)
                    .Select(mapping => mapping.Type)
                    .ToList();
            case HolderEvidenceSource.NCenRegistrant
                when fact.Field == HolderEvidenceFields.InvestmentCompanyType:
                return string.IsNullOrWhiteSpace(fact.Value)
                    ? []
                    : [HolderEntityType.RegisteredFund];
            default:
                return [];
        }
    }

    public static bool IsCurrent(
        HolderClassificationEvidence fact,
        HolderEvidenceContext context
    ) =>
        fact.Source switch
        {
            HolderEvidenceSource.FormAdvRoster => fact.Value switch
            {
                HolderEvidenceFields.AdvRegistered => context.LatestRegisteredRoster is { } latest
                    && fact.AsOf >= latest,
                HolderEvidenceFields.AdvExemptReporting => context.LatestExemptRoster is { } latest
                    && fact.AsOf >= latest,
                _ => false,
            },
            HolderEvidenceSource.Schedule13D or HolderEvidenceSource.Schedule13G => fact.AsOf
                >= context.Today.AddMonths(-BeneficialOwnershipLookbackMonths),
            HolderEvidenceSource.NCenRegistrant => fact.AsOf
                >= context.Today.AddMonths(-NCenLookbackMonths),
            _ => true,
        };

    // 13D and 13G self-reports rank together.
    private static HolderEvidenceSource Rank(HolderEvidenceSource source) =>
        source == HolderEvidenceSource.Schedule13G ? HolderEvidenceSource.Schedule13D : source;

    private static bool IsHoldingCompanyOnly(IReadOnlyList<HolderEntityType> types) =>
        types.All(type => type == HolderEntityType.HoldingCompany);

    // An adviser by SIC or self-report agrees with an adviser on either Form ADV roster, and a
    // parent holding company agrees with whatever else the holder is.
    private static bool HasConflict(IEnumerable<HolderEntityType> types)
    {
        var distinct = types.ToHashSet();
        distinct.Remove(HolderEntityType.HoldingCompany);
        if (
            distinct.Contains(HolderEntityType.RegisteredAdviser)
            || distinct.Contains(HolderEntityType.ExemptReportingAdviser)
        )
            distinct.Remove(HolderEntityType.InvestmentAdviser);
        return distinct.Count > 1;
    }
}
