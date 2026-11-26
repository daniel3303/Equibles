using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Data.Models;

namespace Equibles.Sec.FinancialFacts.Data.Statements;

/// <summary>
/// Diluted EPS a filer tags only as its continuing operations' (Freeport-McMoRan's 10-Ks state
/// 1.52 for 2025 under <c>IncomeLossFromContinuingOperationsPerDilutedShare</c> while its 10-Qs
/// tag <c>EarningsPerShareDiluted</c>), which left the year, its fourth quarter and every trailing
/// EPS absent. That figure is the period's diluted EPS when the filing that states it reports no
/// discontinued operations for the same period, so the diluted EPS alias reads it there, for an
/// exact period none of the alias's own concepts states; a period with discontinued operations
/// keeps no figure rather than the continuing share.
/// </summary>
public static class ContinuingOperationsEps
{
    public const string Alias = "eps-diluted";

    private enum Kind
    {
        Continuing,
        Discontinued,
    }

    private static readonly Dictionary<(FactTaxonomy, string), Kind> KindByConcept = new()
    {
        [(FactTaxonomy.UsGaap, "IncomeLossFromContinuingOperationsPerDilutedShare")] =
            Kind.Continuing,
        [(FactTaxonomy.IfrsFull, "DilutedEarningsLossPerShareFromContinuingOperations")] =
            Kind.Continuing,
        [(FactTaxonomy.UsGaap, "IncomeLossFromDiscontinuedOperationsNetOfTax")] = Kind.Discontinued,
        [
            (
                FactTaxonomy.UsGaap,
                "IncomeLossFromDiscontinuedOperationsNetOfTaxAttributableToReportingEntity"
            )
        ] = Kind.Discontinued,
        [(FactTaxonomy.UsGaap, "IncomeLossFromDiscontinuedOperationsNetOfTaxPerDilutedShare")] =
            Kind.Discontinued,
        [
            (
                FactTaxonomy.UsGaap,
                "IncomeLossFromDiscontinuedOperationsNetOfTaxPerBasicAndDilutedShare"
            )
        ] = Kind.Discontinued,
        [
            (
                FactTaxonomy.UsGaap,
                "DiscontinuedOperationIncomeLossFromDiscontinuedOperationNetOfTax"
            )
        ] = Kind.Discontinued,
        [
            (
                FactTaxonomy.UsGaap,
                "DiscontinuedOperationIncomeLossFromDiscontinuedOperationNetOfTaxPerDilutedShare"
            )
        ] = Kind.Discontinued,
        [
            (
                FactTaxonomy.UsGaap,
                "IncomeLossFromDiscontinuedOperationsNetOfTaxIncludingPortionAttributableToNoncontrollingInterest"
            )
        ] = Kind.Discontinued,
        [(FactTaxonomy.IfrsFull, "ProfitLossFromDiscontinuedOperations")] = Kind.Discontinued,
        [(FactTaxonomy.IfrsFull, "DilutedEarningsLossPerShareFromDiscontinuedOperations")] =
            Kind.Discontinued,
    };

    /// <summary>
    /// The concepts the fold reads beside the alias's own: the continuing operations' diluted EPS
    /// and the discontinued operations' figures that refuse it. They are not members of the alias,
    /// so a reader that folds nothing never shows the continuing share as the whole.
    /// </summary>
    public static IReadOnlyList<FinancialConceptAliases.ConceptRef> EvidenceRefs { get; } =
    [
        .. KindByConcept.Keys.Select(key => new FinancialConceptAliases.ConceptRef(
            key.Item1,
            key.Item2
        )),
    ];

    /// <summary>True for a continuing operations' diluted EPS concept the alias may read.</summary>
    public static bool IsContinuing(FactTaxonomy taxonomy, string tag) =>
        KindByConcept.TryGetValue((taxonomy, tag), out var kind) && kind == Kind.Continuing;

    /// <summary>True for the diluted EPS alias's own resolved family.</summary>
    public static bool IsFamily(IReadOnlyList<FinancialConceptAliases.ConceptRef> concepts) =>
        FinancialConceptAliases.TryResolve(Alias, out var family)
        && ReferenceEquals(family, concepts);

    /// <summary>The ingested concepts the fold tells apart, by id.</summary>
    public sealed class Concepts
    {
        private readonly Dictionary<Guid, Kind> _kinds = [];

        public Concepts(IEnumerable<(Guid Id, FactTaxonomy Taxonomy, string Tag)> concepts)
        {
            foreach (var (id, taxonomy, tag) in concepts)
                if (KindByConcept.TryGetValue((taxonomy, tag), out var kind))
                    _kinds.TryAdd(id, kind);
        }

        public bool IsEvidence(Guid conceptId) => _kinds.ContainsKey(conceptId);

        internal bool IsContinuing(Guid conceptId) =>
            _kinds.GetValueOrDefault(conceptId, Kind.Discontinued) == Kind.Continuing;

        internal bool IsDiscontinued(Guid conceptId) =>
            _kinds.TryGetValue(conceptId, out var kind) && kind == Kind.Discontinued;
    }

    /// <summary>
    /// The alias's facts with each continuing-operations diluted EPS fact added for an exact
    /// period none of them states, where its own filing states no discontinued operations for
    /// that period (none tagged, or zero). Added facts keep their concept and rank after every
    /// concept of the alias, so the alias's own figure always wins.
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
        var discontinued = evidence
            .Where(f =>
                concepts.IsDiscontinued(f.FinancialConceptId)
                && string.IsNullOrEmpty(f.DimensionsKey)
                && f.Value != 0
            )
            .Select(f => (f.AccessionNumber, f.PeriodStart, f.PeriodEnd))
            .ToHashSet();
        var continuing = evidence
            .Where(f =>
                concepts.IsContinuing(f.FinancialConceptId)
                && IsConsolidatedDuration(f)
                && !stated.Contains((f.PeriodStart, f.PeriodEnd))
                && !discontinued.Contains((f.AccessionNumber, f.PeriodStart, f.PeriodEnd))
            )
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

    private static bool IsConsolidatedDuration(FinancialFact fact) =>
        string.IsNullOrEmpty(fact.DimensionsKey)
        && fact.PeriodType == FactPeriodType.Duration
        && fact.PeriodEnd > fact.PeriodStart;
}
