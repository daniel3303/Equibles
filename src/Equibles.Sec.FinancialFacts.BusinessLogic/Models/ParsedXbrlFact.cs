namespace Equibles.Sec.FinancialFacts.BusinessLogic.Models;

/// <summary>
/// A single financial fact extracted from an XBRL instance. Holds the raw
/// concept identity (taxonomy prefix + tag), the numeric value, the unit,
/// the reporting period, and any explicit XBRL dimensions on the context.
/// Consumers map the QName-shaped <see cref="Taxonomy"/> to the persisted
/// <c>FactTaxonomy</c> enum (or keep the raw prefix for filer-extension
/// concepts the enum does not enumerate).
/// </summary>
public class ParsedXbrlFact
{
    /// <summary>
    /// QName prefix of the concept's element (e.g. <c>us-gaap</c>,
    /// <c>dei</c>, <c>srt</c>, <c>ifrs-full</c>, <c>aapl</c>). Preserved
    /// verbatim from the source so filer-extension namespaces survive.
    /// </summary>
    public string Taxonomy { get; init; }

    /// <summary>Local name of the concept element (e.g. <c>Revenues</c>).</summary>
    public string Tag { get; init; }

    /// <summary>
    /// Resolved unit string — single measures collapse to their local name
    /// (<c>iso4217:USD</c> → <c>USD</c>); divide units are emitted as
    /// <c>numerator/denominator</c> (e.g. <c>USD/shares</c>).
    /// </summary>
    public string Unit { get; init; }

    public decimal Value { get; init; }

    /// <summary>True when the context carries <c>xbrli:instant</c>; otherwise the period is a duration.</summary>
    public bool IsInstant { get; init; }

    /// <summary>For instant facts, equals <see cref="PeriodEnd"/>.</summary>
    public DateOnly PeriodStart { get; init; }

    public DateOnly PeriodEnd { get; init; }

    /// <summary>
    /// Explicit XBRL dimensions on this fact's context (axis + member
    /// QNames from <c>xbrldi:explicitMember</c>). Empty list = consolidated
    /// / no-dimension default context — the only context the SEC Company
    /// Facts API returns.
    /// </summary>
    public List<ParsedXbrlDimension> Dimensions { get; init; } = [];

    /// <summary>
    /// XBRL <c>decimals</c> attribute, when present — the reported
    /// precision (positive for fractional digits, negative for rounding
    /// magnitude, <c>INF</c> → <c>int.MaxValue</c>). Null when absent.
    /// </summary>
    public int? Decimals { get; init; }
}
