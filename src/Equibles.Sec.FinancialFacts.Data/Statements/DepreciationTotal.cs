using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Data.Models;

namespace Equibles.Sec.FinancialFacts.Data.Statements;

/// <summary>
/// A period's depreciation and amortization is one figure, and a filer can tag it under several
/// concepts at once: the total beside a slice of it (McDonald's FY2025: 2,199M under
/// <c>DepreciationAndAmortization</c>, 457M under <c>DepreciationDepletionAndAmortization</c>), or
/// beside the same charge with the other sign (AES FY2025: 1,457M against -1,457M). Those two
/// concepts name one measure, so in a fiscal year where both state a figure the larger is the total
/// and the smaller a slice. <c>DepreciationAmortizationAndAccretionNet</c> is that charge plus
/// accretion: a total below it is expected and stands (Entergy FY2025: 2,305.6M under 2,537.1M),
/// while an accretion-net figure below a total is a slice too. A slice is left out of every period
/// inside its fiscal year, so a year never reads one concept and its quarters another. A filer that
/// tags no total gets the sum of its reported depreciation and amortization of intangible assets
/// (Microsoft FY2026: 34,300M plus 4,700M).
/// </summary>
public static class DepreciationTotal
{
    public const string Alias = "depreciation-and-amortization";

    /// <summary>What a reader says beside a figure that <see cref="IsSum"/>.</summary>
    public const string SumNote =
        "Depreciation & amortization is the sum of the reported depreciation and the reported "
        + "amortization of intangible assets where the filer tags no total.";

    /// <summary>
    /// The concept id every summed fact carries. No stored concept has it, so a sum never collides
    /// with a part's own facts and its derived quarters subtract like any one concept.
    /// </summary>
    public static readonly Guid ConceptId = new("7d3c9a52-1e4b-4f68-b0a7-5c8e2d6f9b14");

    private enum Kind
    {
        DepletionTotal,
        PlainTotal,
        AccretionTotal,
        Depreciation,
        Amortization,
    }

    private static readonly Dictionary<string, Kind> KindByTag = new(StringComparer.Ordinal)
    {
        ["DepreciationDepletionAndAmortization"] = Kind.DepletionTotal,
        ["DepreciationAndAmortization"] = Kind.PlainTotal,
        ["DepreciationAmortizationAndAccretionNet"] = Kind.AccretionTotal,
        ["Depreciation"] = Kind.Depreciation,
        ["AmortizationOfIntangibleAssets"] = Kind.Amortization,
    };

    /// <summary>
    /// The two parts summed for a filer without a total. They are not members of the alias, so a
    /// reader that folds nothing never shows depreciation alone as the whole charge; a caller loads
    /// them beside the alias's own concepts.
    /// </summary>
    public static IReadOnlyList<FinancialConceptAliases.ConceptRef> PartRefs { get; } =
    [
        new(FactTaxonomy.UsGaap, "Depreciation"),
        new(FactTaxonomy.UsGaap, "AmortizationOfIntangibleAssets"),
    ];

    /// <summary>
    /// The ingested concepts the fold tells apart. Several ids can share one role: a respelled
    /// concept is separate rows, and a caller that resolves them passes each under its tag.
    /// </summary>
    public sealed class Concepts
    {
        private readonly Dictionary<Guid, Kind> _kinds = [];

        public Concepts(IEnumerable<(Guid Id, FactTaxonomy Taxonomy, string Tag)> concepts)
        {
            foreach (var (id, taxonomy, tag) in concepts)
                if (taxonomy == FactTaxonomy.UsGaap && KindByTag.TryGetValue(tag, out var kind))
                    _kinds.TryAdd(id, kind);
        }

        public Concepts(
            IReadOnlyDictionary<(FactTaxonomy Taxonomy, string Tag), Guid> conceptIdByKey
        )
            : this(conceptIdByKey.Select(pair => (pair.Value, pair.Key.Taxonomy, pair.Key.Tag))) { }

        /// <summary>True for a concept of <see cref="PartRefs"/>.</summary>
        public bool IsPart(Guid conceptId) =>
            _kinds.TryGetValue(conceptId, out var kind)
            && kind is Kind.Depreciation or Kind.Amortization;

        internal bool IsDepreciation(Guid conceptId) =>
            _kinds.TryGetValue(conceptId, out var kind) && kind == Kind.Depreciation;

        internal bool IsAmortization(Guid conceptId) =>
            _kinds.TryGetValue(conceptId, out var kind) && kind == Kind.Amortization;

        internal int? TotalOf(Guid conceptId) =>
            _kinds.TryGetValue(conceptId, out var kind)
            && kind is Kind.DepletionTotal or Kind.PlainTotal or Kind.AccretionTotal
                ? (int)kind
                : null;
    }

    /// <summary>
    /// True when <paramref name="concepts"/> is the depreciation-and-amortization alias (or one of
    /// its synonyms, which resolve to the same list). A statement line built on the alias's own
    /// list is the family under any line name.
    /// </summary>
    public static bool IsFamily(IReadOnlyList<FinancialConceptAliases.ConceptRef> concepts) =>
        FinancialConceptAliases.TryResolve(Alias, out var family)
        && ReferenceEquals(family, concepts);

    /// <summary>True for a figure this class summed from the two parts.</summary>
    public static bool IsSum(FinancialFact fact) => fact.FinancialConceptId == ConceptId;

    /// <summary>
    /// The alias's facts without the slices, plus the sum of the two parts in every fiscal year the
    /// alias states nothing, and the pick priority that goes with them. A sum pairs the parts one
    /// filing states for one exact period and ranks after every concept of the alias. Each fiscal
    /// year keeps one measure, and a summed period never gives way to a total filed later.
    /// <paramref name="parts"/> holds the facts of <see cref="PartRefs"/>.
    /// </summary>
    public static (List<FinancialFact> Facts, Dictionary<Guid, int> Priority) Fold(
        IReadOnlyCollection<FinancialFact> family,
        IReadOnlyCollection<FinancialFact> parts,
        Concepts concepts,
        IReadOnlyDictionary<Guid, int> familyPriority
    )
    {
        var priority = new Dictionary<Guid, int>(familyPriority);
        var kept = WithoutSlices(family, concepts);
        var statedYears = kept.Where(IsConsolidatedDuration).Select(f => f.FiscalYear).ToHashSet();
        var allSums = Sums(parts, concepts);
        // After a year the alias states, the next year's quarters wait for its annual report: a
        // filer that tags its total only there would show summed quarters that vanish when the
        // total arrives.
        var closedYears = allSums
            .Where(f => Span(f) >= FiscalPeriodSpanDays.MinAnnual)
            .Select(f => f.FiscalYear)
            .ToHashSet();
        var sums = allSums
            .Where(f =>
                !statedYears.Contains(f.FiscalYear)
                && (!statedYears.Contains(f.FiscalYear - 1) || closedYears.Contains(f.FiscalYear))
            )
            .ToList();
        if (sums.Count == 0)
            return (kept, priority);

        priority[ConceptId] = int.MaxValue;
        return ([.. kept, .. sums], priority);
    }

    /// <summary>
    /// A statement's facts as <see cref="Fold"/> reads the issuer's history: the alias's slices left
    /// out and the sums of <paramref name="fiscalYear"/> added, so a statement shows the figure a
    /// series shows for the same period. <paramref name="folded"/> is the fold of the issuer's
    /// consolidated facts across every year; dimensional facts pass through untouched.
    /// </summary>
    public static List<FinancialFact> ForStatement(
        IReadOnlyCollection<FinancialFact> statementFacts,
        int fiscalYear,
        IReadOnlyCollection<FinancialFact> folded,
        Concepts concepts
    )
    {
        var keptIds = folded.Where(f => !IsSum(f)).Select(f => f.Id).ToHashSet();
        return
        [
            .. statementFacts.Where(f =>
                concepts.TotalOf(f.FinancialConceptId) == null
                || f.DimensionsKey != ""
                || keptIds.Contains(f.Id)
            ),
            .. folded.Where(f => IsSum(f) && f.FiscalYear == fiscalYear),
        ];
    }

    private sealed record Stated(int Kind, FinancialFact Fact)
    {
        public (int Kind, DateOnly Start, DateOnly End, string Unit) Key =>
            (Kind, Fact.PeriodStart, Fact.PeriodEnd, Fact.Unit);
    }

    // A slice follows its fiscal year: left out of every period inside a year it lost, and out of
    // the open periods after its newest judged year when it lost that one, so a rolling window never
    // adds one concept's year to another's quarters.
    private static List<FinancialFact> WithoutSlices(
        IReadOnlyCollection<FinancialFact> family,
        Concepts concepts
    )
    {
        var stated = family
            .Where(IsConsolidatedDuration)
            .Select(f => (Fact: f, Kind: concepts.TotalOf(f.FinancialConceptId)))
            .Where(item => item.Kind != null)
            .GroupBy(item =>
                (Kind: item.Kind.Value, item.Fact.PeriodStart, item.Fact.PeriodEnd, item.Fact.Unit)
            )
            .Select(period => new Stated(
                period.Key.Kind,
                period
                    .Select(item => item.Fact)
                    .OrderBy(f => FinancialFactSourcePriority.Rank(f.Form))
                    .ThenByDescending(f => f.FiledDate)
                    .ThenByDescending(f => f.AccessionNumber, StringComparer.Ordinal)
                    .First()
            ))
            .ToList();
        var slices = stated
            .GroupBy(item => (item.Fact.PeriodStart, item.Fact.PeriodEnd, item.Fact.Unit))
            .SelectMany(period => period.Where(item => period.Any(other => IsSliceOf(item, other))))
            .Select(item => item.Key)
            .ToHashSet();
        if (slices.Count == 0)
            return [.. family];

        var years = stated
            .Where(item => Span(item.Fact) >= FiscalPeriodSpanDays.MinAnnual)
            .Select(item => (Year: item, Slice: slices.Contains(item.Key)))
            .ToList();

        bool IsSlice(int kind, FinancialFact fact)
        {
            var own = years
                .Where(item =>
                    item.Year.Kind == kind
                    && string.Equals(item.Year.Fact.Unit, fact.Unit, StringComparison.Ordinal)
                )
                .ToList();
            var covering = own.Where(item =>
                    item.Year.Fact.PeriodStart <= fact.PeriodStart
                    && fact.PeriodEnd <= item.Year.Fact.PeriodEnd
                )
                .OrderBy(item => Span(item.Year.Fact))
                .ToList();
            if (covering.Count > 0)
                return covering[0].Slice;

            var judgedBefore = own.Where(item => item.Year.Fact.PeriodEnd < fact.PeriodEnd)
                .OrderByDescending(item => item.Year.Fact.PeriodEnd)
                .ToList();
            return (judgedBefore.Count > 0 && judgedBefore[0].Slice)
                || slices.Contains((kind, fact.PeriodStart, fact.PeriodEnd, fact.Unit));
        }

        return family
            .Where(f =>
                concepts.TotalOf(f.FinancialConceptId) is not { } kind
                || !IsConsolidatedDuration(f)
                || !IsSlice(kind, f)
            )
            .ToList();
    }

    // Two totals name one measure, so the smaller is a slice of the larger. A total below the
    // accretion-net figure is the same charge without accretion, not a slice of it, as long as it
    // states a charge at all.
    private static bool IsSliceOf(Stated item, Stated other) =>
        other.Kind != item.Kind
        && other.Fact.Value > item.Fact.Value
        && !(
            item.Kind != (int)Kind.AccretionTotal
            && other.Kind == (int)Kind.AccretionTotal
            && item.Fact.Value > 0m
        );

    private static List<FinancialFact> Sums(
        IReadOnlyCollection<FinancialFact> parts,
        Concepts concepts
    )
    {
        var amortizationByFiling = parts
            .Where(f => concepts.IsAmortization(f.FinancialConceptId) && IsConsolidatedDuration(f))
            .GroupBy(f => (f.AccessionNumber, f.PeriodStart, f.PeriodEnd, f.Unit))
            .ToDictionary(g => g.Key, g => g.OrderByDescending(f => f.FiledDate).First());
        return parts
            .Where(f => concepts.IsDepreciation(f.FinancialConceptId) && IsConsolidatedDuration(f))
            .GroupBy(f => (f.AccessionNumber, f.PeriodStart, f.PeriodEnd, f.Unit))
            .Select(g =>
                amortizationByFiling.TryGetValue(g.Key, out var amortization)
                    ? Sum(g.OrderByDescending(f => f.FiledDate).First(), amortization)
                    : null
            )
            .OfType<FinancialFact>()
            .ToList();
    }

    // Each part is a charge, so a zero or negative one states none; a copy keeps EF-tracked parts
    // untouched.
    private static FinancialFact Sum(FinancialFact depreciation, FinancialFact amortization)
    {
        if (depreciation.Value <= 0m || amortization.Value <= 0m)
            return null;

        var stating =
            depreciation.FiledDate >= amortization.FiledDate ? depreciation : amortization;
        var sum = stating.Restamped(depreciation.FiscalYear, depreciation.FiscalPeriod);
        sum.FinancialConceptId = ConceptId;
        sum.Value = depreciation.Value + amortization.Value;
        if (
            StatementQuarterDerivation.IsDerived(depreciation)
            || StatementQuarterDerivation.IsDerived(amortization)
        )
        {
            sum.Form = null;
            sum.AccessionNumber = null;
            sum.DocumentId = null;
        }
        return sum;
    }

    private static bool IsConsolidatedDuration(FinancialFact fact) =>
        fact.PeriodType == FactPeriodType.Duration
        && fact.DimensionsKey == ""
        && Span(fact) is > 0 and <= FiscalPeriodSpanDays.MaxAnnual;

    private static int Span(FinancialFact fact) =>
        fact.PeriodEnd.DayNumber - fact.PeriodStart.DayNumber;
}
