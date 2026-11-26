using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Data.Models;

namespace Equibles.Sec.FinancialFacts.Data.Statements;

/// <summary>
/// Diluted EPS a filer tags only as its continuing operations' (Freeport-McMoRan's 10-K states
/// 1.52 for 2025 under <c>IncomeLossFromContinuingOperationsPerDilutedShare</c> while its 10-Qs
/// tag <c>EarningsPerShareDiluted</c>), which left the year, its fourth quarter and every trailing
/// EPS absent. The diluted EPS alias reads that figure for an exact period none of its own
/// concepts states, only where the filing that states it proves it the whole for that period:
/// no discontinued operations, continuing income equal to net income, and the figure times the
/// diluted shares equal to the income for common shareholders. Absent any of the three, a filing
/// that tags both figures shows them apart far too often (one in ten with no discontinued
/// operations tagged), so the period keeps no figure.
/// </summary>
public static class ContinuingOperationsEps
{
    public const string Alias = "eps-diluted";

    internal enum Kind
    {
        Continuing,
        Discontinued,
        ContinuingIncome,
        NetIncome,
        ContinuingIncomeWithMinorityInterests,
        ProfitWithMinorityInterests,
        DilutedShares,
        CommonIncome,
    }

    // US GAAP only: the proof was measured on these concepts, and no IFRS equivalents of them.
    private static readonly Dictionary<string, Kind> KindByTag = new()
    {
        ["IncomeLossFromContinuingOperationsPerDilutedShare"] = Kind.Continuing,
        ["IncomeLossFromDiscontinuedOperationsNetOfTax"] = Kind.Discontinued,
        ["IncomeLossFromDiscontinuedOperationsNetOfTaxAttributableToReportingEntity"] =
            Kind.Discontinued,
        ["IncomeLossFromDiscontinuedOperationsNetOfTaxPerDilutedShare"] = Kind.Discontinued,
        ["IncomeLossFromDiscontinuedOperationsNetOfTaxPerBasicAndDilutedShare"] = Kind.Discontinued,
        ["DiscontinuedOperationIncomeLossFromDiscontinuedOperationNetOfTax"] = Kind.Discontinued,
        ["DiscontinuedOperationIncomeLossFromDiscontinuedOperationNetOfTaxPerDilutedShare"] =
            Kind.Discontinued,
        [
            "IncomeLossFromDiscontinuedOperationsNetOfTaxIncludingPortionAttributableToNoncontrollingInterest"
        ] = Kind.Discontinued,
        ["IncomeLossFromContinuingOperations"] = Kind.ContinuingIncome,
        ["NetIncomeLoss"] = Kind.NetIncome,
        ["IncomeLossFromContinuingOperationsIncludingPortionAttributableToNoncontrollingInterest"] =
            Kind.ContinuingIncomeWithMinorityInterests,
        ["ProfitLoss"] = Kind.ProfitWithMinorityInterests,
        ["WeightedAverageNumberOfDilutedSharesOutstanding"] = Kind.DilutedShares,
        ["NetIncomeLossAvailableToCommonStockholdersDiluted"] = Kind.CommonIncome,
    };

    /// <summary>
    /// The concepts the fold reads beside the alias's own: the continuing operations' diluted EPS
    /// and the figures of its filing that prove or refuse it. None is a member of the alias, so a
    /// reader that folds nothing never shows the continuing share as the whole.
    /// </summary>
    public static IReadOnlyList<FinancialConceptAliases.ConceptRef> EvidenceRefs { get; } =
    [
        .. KindByTag.Keys.Select(tag => new FinancialConceptAliases.ConceptRef(
            FactTaxonomy.UsGaap,
            tag
        )),
    ];

    /// <summary>The continuing operations' diluted EPS concept a statement line reads a proved figure under.</summary>
    public static FinancialConceptAliases.ConceptRef ContinuingRef { get; } =
        new(FactTaxonomy.UsGaap, "IncomeLossFromContinuingOperationsPerDilutedShare");

    /// <summary>True for a continuing operations' diluted EPS concept the alias may read.</summary>
    public static bool IsContinuing(FactTaxonomy taxonomy, string tag) =>
        KindOfConcept(taxonomy, tag) == Kind.Continuing;

    /// <summary>True for the diluted EPS alias's own resolved family.</summary>
    public static bool IsFamily(IReadOnlyList<FinancialConceptAliases.ConceptRef> concepts) =>
        FinancialConceptAliases.TryResolve(Alias, out var family)
        && ReferenceEquals(family, concepts);

    private static Kind? KindOfConcept(FactTaxonomy taxonomy, string tag) =>
        taxonomy == FactTaxonomy.UsGaap && KindByTag.TryGetValue(tag, out var kind) ? kind : null;

    /// <summary>The ingested concepts the fold tells apart, by id.</summary>
    public sealed class Concepts
    {
        private readonly Dictionary<Guid, Kind> _kinds = [];

        public Concepts(IEnumerable<(Guid Id, FactTaxonomy Taxonomy, string Tag)> concepts)
        {
            foreach (var (id, taxonomy, tag) in concepts)
                if (KindOfConcept(taxonomy, tag) is { } kind)
                    _kinds.TryAdd(id, kind);
        }

        public bool IsEvidence(Guid conceptId) => _kinds.ContainsKey(conceptId);

        public bool IsContinuing(Guid conceptId) => KindOf(conceptId) == Kind.Continuing;

        internal Kind? KindOf(Guid conceptId) =>
            _kinds.TryGetValue(conceptId, out var kind) ? kind : null;
    }

    /// <summary>
    /// The alias's facts with each continuing-operations diluted EPS fact added for an exact
    /// period none of them states, where its own filing proves it the whole for that period
    /// (<see cref="ContinuingOperationsEps"/>). Added facts keep their concept and rank after
    /// every concept of the alias, so the alias's own figure always wins.
    /// </summary>
    public static (List<FinancialFact> Facts, Dictionary<Guid, int> Priority) Fold(
        IReadOnlyCollection<FinancialFact> eps,
        IReadOnlyCollection<FinancialFact> evidence,
        Concepts concepts,
        IReadOnlyDictionary<Guid, int> priority
    )
    {
        var folded = new List<FinancialFact>(eps);
        var rank = new Dictionary<Guid, int>(priority);
        var stated = eps.Where(IsConsolidatedDuration)
            .Select(f => (f.PeriodStart, f.PeriodEnd))
            .ToHashSet();
        var filed = evidence
            .Where(IsConsolidatedDuration)
            .Select(f => (Fact: f, Kind: concepts.KindOf(f.FinancialConceptId)))
            .Where(item => item.Kind != null)
            .ToLookup(
                item => (item.Fact.AccessionNumber, item.Fact.PeriodStart, item.Fact.PeriodEnd),
                item => (item.Fact, Kind: item.Kind.Value)
            );
        var continuing = filed
            .SelectMany(group => group)
            .Where(item =>
                item.Kind == Kind.Continuing
                && !stated.Contains((item.Fact.PeriodStart, item.Fact.PeriodEnd))
                && ProvesTheWhole(
                    item.Fact,
                    filed[(item.Fact.AccessionNumber, item.Fact.PeriodStart, item.Fact.PeriodEnd)]
                )
            )
            .Select(item => item.Fact)
            .ToList();
        if (continuing.Count == 0)
            return (folded, rank);
        // Set, not kept: a caller's loader may rank the evidence concepts among its own.
        var after = priority.Count == 0 ? 0 : priority.Values.Max() + 1;
        foreach (var fact in continuing)
        {
            folded.Add(fact);
            rank[fact.FinancialConceptId] = after;
        }
        return (folded, rank);
    }

    /// <summary>
    /// A statement's facts as <see cref="Fold"/> reads the issuer's history: the continuing
    /// operations' figures of <paramref name="fiscalYear"/> its filings prove the whole, and no
    /// other, so a statement shows the diluted EPS a series shows for the same period.
    /// <paramref name="folded"/> is the fold of the issuer's consolidated facts across every year.
    /// </summary>
    public static List<FinancialFact> ForStatement(
        IReadOnlyCollection<FinancialFact> statementFacts,
        int fiscalYear,
        IReadOnlyCollection<FinancialFact> folded,
        Concepts concepts
    ) =>
        [
            .. statementFacts.Where(f => !concepts.IsContinuing(f.FinancialConceptId)),
            .. folded.Where(f =>
                concepts.IsContinuing(f.FinancialConceptId) && f.FiscalYear == fiscalYear
            ),
        ];

    // The three proofs, each read from the same filing and exact period, values as filed.
    private static bool ProvesTheWhole(
        FinancialFact eps,
        IEnumerable<(FinancialFact Fact, Kind Kind)> filing
    )
    {
        var byKind = filing.ToLookup(item => item.Kind, item => item.Fact);
        if (byKind[Kind.Discontinued].Any(f => f.Value != 0))
            return false;
        if (
            !Equal(byKind[Kind.ContinuingIncome], byKind[Kind.NetIncome])
            && !Equal(
                byKind[Kind.ContinuingIncomeWithMinorityInterests],
                byKind[Kind.ProfitWithMinorityInterests]
            )
        )
            return false;
        var currency = eps.Unit?.Split('/')[0];
        var incomes = (
            byKind[Kind.CommonIncome].Any() ? byKind[Kind.CommonIncome] : byKind[Kind.NetIncome]
        )
            .Where(f => f.Unit == currency)
            .ToList();
        var shares = byKind[Kind.DilutedShares]
            .Where(f => string.Equals(f.Unit, "shares", StringComparison.OrdinalIgnoreCase))
            .Where(f => f.Value > 0)
            .ToList();
        // A figure stated to the cent is off by up to half a cent a share, plus a sliver of income.
        return shares.Any(s =>
            incomes.Any(income =>
                Math.Abs(eps.Value * s.Value - income.Value)
                <= 0.006m * s.Value + 0.005m * Math.Abs(income.Value)
            )
        );
    }

    private static bool Equal(IEnumerable<FinancialFact> left, IEnumerable<FinancialFact> right) =>
        left.Any(l => right.Any(r => r.Unit == l.Unit && r.Value == l.Value));

    private static bool IsConsolidatedDuration(FinancialFact fact) =>
        string.IsNullOrEmpty(fact.DimensionsKey)
        && fact.PeriodType == FactPeriodType.Duration
        && fact.PeriodEnd > fact.PeriodStart;
}
