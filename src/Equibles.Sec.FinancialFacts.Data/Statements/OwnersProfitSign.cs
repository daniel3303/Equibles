using Equibles.Sec.Data.Models;
using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Data.Models;

namespace Equibles.Sec.FinancialFacts.Data.Statements;

/// <summary>
/// A foreign filer can tag its owners' (parent-attributable) profit with the opposite sign of the
/// one it prints, in every period it reports, while its total profit and minority share carry the
/// printed ones (PicPay tags each profit as a loss, Solmate each loss as a profit). A tagged
/// owners' figure is read negated where the same filing's figures for the same exact period prove
/// it: total profit equals owners' profit plus the minority share only once the owners' figure is
/// negated. Once any filing proves the tag mis-signed in a unit, that unit keeps only figures
/// their own filing proves, flipped or as tagged, so a twelve-month sum or a derived quarter
/// never mixes the two signs. Only a filer of 20-F, 40-F or 6-K reports is read this way: the
/// rule was measured on foreign filers alone.
/// </summary>
public static class OwnersProfitSign
{
    /// <summary>What a reading says about owners' profit figures it negated.</summary>
    public const string NegatedNote =
        "Net income is read with the sign the filing's own figures prove: its total profit equals "
        + "the owners' profit plus the non-controlling interests' share only with that sign.";

    private enum Kind
    {
        Total,
        Minority,
        MinoritySplit,
        BasicEps,

        // Stated where basic and diluted are one figure; read only when the filing tags no basic figure.
        BasicAndDilutedEps,
    }

    private const string SharesUnit = "shares";

    private static readonly Dictionary<(FactTaxonomy Taxonomy, string Tag), Kind> KindByConcept =
        new()
        {
            [(FactTaxonomy.UsGaap, "ProfitLoss")] = Kind.Total,
            [(FactTaxonomy.IfrsFull, "ProfitLoss")] = Kind.Total,
            [(FactTaxonomy.UsGaap, "NetIncomeLossAttributableToNoncontrollingInterest")] =
                Kind.Minority,
            [(FactTaxonomy.IfrsFull, "ProfitLossAttributableToNoncontrollingInterests")] =
                Kind.Minority,
            // A filer that splits its minority share into redeemable and other holders may tag
            // either alone, so the plain minority tag is no longer the whole share.
            [
                (FactTaxonomy.UsGaap, "NetIncomeLossAttributableToRedeemableNoncontrollingInterest")
            ] = Kind.MinoritySplit,
            [
                (
                    FactTaxonomy.UsGaap,
                    "NetIncomeLossAttributableToNonredeemableNoncontrollingInterest"
                )
            ] = Kind.MinoritySplit,
            [(FactTaxonomy.UsGaap, "EarningsPerShareBasic")] = Kind.BasicEps,
            [(FactTaxonomy.IfrsFull, "BasicEarningsLossPerShare")] = Kind.BasicEps,
            [(FactTaxonomy.UsGaap, "EarningsPerShareBasicAndDiluted")] = Kind.BasicAndDilutedEps,
            [(FactTaxonomy.IfrsFull, "BasicAndDilutedEarningsLossPerShare")] =
                Kind.BasicAndDilutedEps,
        };

    private static readonly HashSet<(FactTaxonomy Taxonomy, string Tag)> OwnersConcepts =
    [
        (FactTaxonomy.UsGaap, "NetIncomeLoss"),
        (FactTaxonomy.IfrsFull, "ProfitLossAttributableToOwnersOfParent"),
    ];

    private static readonly HashSet<DocumentType> ForeignIssuerForms =
    [
        DocumentType.TwentyF,
        DocumentType.TwentyFa,
        DocumentType.FortyF,
        DocumentType.FortyFa,
        DocumentType.SixK,
        DocumentType.SixKa,
    ];

    /// <summary>The concepts whose facts a caller loads beside the owners' profit to judge it.</summary>
    public static IReadOnlyList<(FactTaxonomy Taxonomy, string Tag)> EvidencePairs { get; } =
        KindByConcept.Keys.ToList();

    /// <summary>True for the owners' (parent-attributable) profit concepts the reading judges.</summary>
    public static bool IsOwnersProfit(FactTaxonomy taxonomy, string tag) =>
        OwnersConcepts.Contains((taxonomy, tag));

    /// <summary>
    /// The owners' (parent-attributable) profit concepts the reading judges, as (taxonomy, tag).
    /// </summary>
    public static IReadOnlyCollection<(FactTaxonomy Taxonomy, string Tag)> OwnersPairs =>
        OwnersConcepts;

    /// <summary>
    /// False when no fact is a foreign issuer's report: the reading changes nothing then, so a
    /// caller can skip loading its evidence.
    /// </summary>
    public static bool MayApply(IEnumerable<FinancialFact> lineFacts) =>
        lineFacts.Any(fact => ForeignIssuerForms.Contains(fact.Form));

    /// <summary>True when <paramref name="concepts"/> is the net-income alias (or a synonym).</summary>
    public static bool IsNetIncome(IReadOnlyList<FinancialConceptAliases.ConceptRef> concepts) =>
        FinancialConceptAliases.TryResolve("net-income", out var netIncome)
        && ReferenceEquals(netIncome, concepts);

    /// <summary>
    /// The line's facts as read: each owners' figure (a concept in
    /// <paramref name="ownersConceptIds"/>) with the sign its filing proves, less every figure of a
    /// period no filing can check in a unit where a filing proved the tag mis-signed; the line's
    /// other concepts (total profit) leave that period too, so it is absent rather than another
    /// measure. <c>Negated</c> holds the negated copies, by reference. <paramref name="evidence"/>
    /// holds the facts of <see cref="EvidencePairs"/>, each concept under the pair it resolves to.
    /// A caller reading one period passes the issuer's whole owners' profit history as
    /// <paramref name="ownersHistory"/>, so it finds the units another year's filing proved
    /// mis-signed.
    /// </summary>
    public static Reading Read(
        IReadOnlyCollection<FinancialFact> lineFacts,
        IReadOnlySet<Guid> ownersConceptIds,
        IReadOnlyCollection<FinancialFact> evidence,
        IReadOnlyDictionary<Guid, (FactTaxonomy Taxonomy, string Tag)> evidenceConcepts,
        IReadOnlyCollection<FinancialFact> ownersHistory = null
    )
    {
        var unchanged = new Reading(
            [.. lineFacts],
            new HashSet<FinancialFact>(),
            new HashSet<(DateOnly, DateOnly, string)>()
        );
        var ownersFacts = lineFacts
            .Where(fact => ownersConceptIds.Contains(fact.FinancialConceptId))
            .ToList();
        List<FinancialFact> pool =
        [
            .. ownersFacts,
            .. (ownersHistory ?? []).Where(fact =>
                ownersConceptIds.Contains(fact.FinancialConceptId)
            ),
        ];
        if (!pool.Any(fact => ForeignIssuerForms.Contains(fact.Form)))
            return unchanged;
        var filings = evidence
            .Where(IsConsolidatedSpanOfAFiling)
            .Select(fact =>
                (
                    Fact: fact,
                    Kind: evidenceConcepts.TryGetValue(fact.FinancialConceptId, out var concept)
                    && KindByConcept.TryGetValue(concept, out var kind)
                        ? kind
                        : (Kind?)null
                )
            )
            .Where(item => item.Kind != null)
            .ToLookup(
                item => (item.Fact.AccessionNumber, item.Fact.PeriodStart, item.Fact.PeriodEnd),
                item => (item.Fact, item.Kind.Value)
            );
        if (filings.Count == 0)
            return unchanged;
        // One owners' figure per filing, period and unit: two different ones prove nothing.
        var tagged = pool.Where(IsConsolidatedSpanOfAFiling)
            .ToLookup(
                fact => (fact.AccessionNumber, fact.PeriodStart, fact.PeriodEnd, fact.Unit),
                fact => fact.Value
            );
        var signs = new Dictionary<FinancialFact, Sign?>(ReferenceEqualityComparer.Instance);
        foreach (var fact in pool)
            signs[fact] =
                !IsConsolidatedSpanOfAFiling(fact)
                || tagged[(fact.AccessionNumber, fact.PeriodStart, fact.PeriodEnd, fact.Unit)]
                    .Distinct()
                    .Count() != 1
                    ? null
                    : new Filing([
                        .. filings[(fact.AccessionNumber, fact.PeriodStart, fact.PeriodEnd)],
                    ]).ProvenSign(fact.Value, fact.Unit);
        var misSignedUnits = signs
            .Where(pair => pair.Value == Sign.Opposite)
            .Select(pair => pair.Key.Unit)
            .ToHashSet(StringComparer.Ordinal);
        if (misSignedUnits.Count == 0)
            return unchanged;
        var withheld = signs
            .Where(pair =>
                pair.Value == null
                && IsConsolidatedSpanOfAFiling(pair.Key)
                && misSignedUnits.Contains(pair.Key.Unit)
            )
            .Select(pair => (pair.Key.PeriodStart, pair.Key.PeriodEnd, pair.Key.Unit))
            .ToHashSet();
        var negated = new HashSet<FinancialFact>(ReferenceEqualityComparer.Instance);
        var read = new List<FinancialFact>();
        foreach (var fact in lineFacts)
        {
            if (
                IsConsolidatedSpanOfAFiling(fact)
                && withheld.Contains((fact.PeriodStart, fact.PeriodEnd, fact.Unit))
            )
                continue;
            if (!signs.TryGetValue(fact, out var sign) || sign != Sign.Opposite)
            {
                read.Add(fact);
                continue;
            }
            var copy = fact.Restamped(fact.FiscalYear, fact.FiscalPeriod);
            copy.Value = -fact.Value;
            negated.Add(copy);
            read.Add(copy);
        }
        return new Reading(read, negated, withheld);
    }

    /// <summary>
    /// The line's facts as read (<see cref="Read"/>), the negated copies among them and the
    /// periods left out, each with its unit.
    /// </summary>
    public sealed record Reading(
        List<FinancialFact> Facts,
        IReadOnlySet<FinancialFact> Negated,
        IReadOnlySet<(DateOnly Start, DateOnly End, string Unit)> WithheldPeriods
    );

    private enum Sign
    {
        AsTagged,
        Opposite,
    }

    // One filing's tagged figures for one exact period.
    private sealed class Filing(List<(FinancialFact Fact, Kind Kind)> items)
    {
        private IEnumerable<FinancialFact> Facts(Kind kind, string unit) =>
            items
                .Where(item =>
                    item.Kind == kind
                    && string.Equals(item.Fact.Unit, unit, StringComparison.Ordinal)
                )
                .Select(item => item.Fact);

        private bool Any(Kind kind, string unit) => Facts(kind, unit).Any();

        // One figure per line: two different values under one tag, filing, period and unit prove nothing.
        private decimal? One(Kind kind, string unit)
        {
            var values = Facts(kind, unit).Select(fact => fact.Value).Distinct().ToList();
            return values.Count == 1 ? values[0] : null;
        }

        // The sign the filing's own figures prove for `owners`: total profit equals the minority
        // share plus it (as tagged) or less it (opposite), and not both, each within the three
        // figures' rounding; null when they prove neither. A total or a minority share inside that
        // rounding proves nothing: negating it would satisfy the identity as well. A basic EPS of
        // the tagged sign refuses the opposite one.
        public Sign? ProvenSign(decimal owners, string unit)
        {
            if (
                Any(Kind.MinoritySplit, unit)
                || One(Kind.Total, unit) is not { } total
                || One(Kind.Minority, unit) is not { } minority
            )
                return null;
            // Each figure is rounded by up to half its unit.
            var rounding =
                1.5m * new[] { total, minority, owners }.Max(GrossProfitMistag.RoundingUnit);
            if (Math.Abs(total) <= rounding || Math.Abs(minority) <= rounding)
                return null;
            var asTagged = Math.Abs(total - (minority + owners)) <= rounding;
            var opposite = Math.Abs(total - (minority - owners)) <= rounding;
            if (asTagged == opposite)
                return null;
            if (asTagged)
                return Sign.AsTagged;
            var perShare = unit + "/" + SharesUnit;
            var eps = Any(Kind.BasicEps, perShare)
                ? One(Kind.BasicEps, perShare)
                : One(Kind.BasicAndDilutedEps, perShare);
            return eps is null or 0m || Math.Sign(eps.Value) != Math.Sign(owners)
                ? Sign.Opposite
                : null;
        }
    }

    private static bool IsConsolidatedSpanOfAFiling(FinancialFact fact) =>
        fact.PeriodType == FactPeriodType.Duration
        && fact.DimensionsKey == ""
        && fact.PeriodStart < fact.PeriodEnd
        && !string.IsNullOrEmpty(fact.AccessionNumber);
}
