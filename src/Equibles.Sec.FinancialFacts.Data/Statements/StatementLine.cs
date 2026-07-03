using Equibles.Sec.FinancialFacts.Data.Enums;

namespace Equibles.Sec.FinancialFacts.Data.Statements;

/// <summary>
/// One line of a financial statement: the ordered XBRL concept variants that
/// express it (<see cref="Concepts"/>, resolved from an alias in
/// <see cref="FinancialConceptAliases"/>) and the human-readable
/// <see cref="Label"/> to render for it. Consumers pick the first variant a
/// company reported for the requested period — earlier concepts are the
/// broader/preferred measure, later ones only fill gaps (a tag transition or a
/// narrower variant like ADBE's software-specific R&amp;D tag). Ordering within
/// a statement is the order these are declared in
/// <see cref="FinancialStatementConcepts"/>.
/// </summary>
public class StatementLine
{
    public StatementLine(
        string alias,
        string label,
        IReadOnlyList<FinancialConceptAliases.ConceptRef> concepts
    )
    {
        Alias = alias;
        Label = label;
        Concepts = concepts;
    }

    /// <summary>The <see cref="FinancialConceptAliases"/> key the line resolves through.</summary>
    public string Alias { get; }

    public string Label { get; }

    /// <summary>Ordered tag variants; the first is the preferred concept.</summary>
    public IReadOnlyList<FinancialConceptAliases.ConceptRef> Concepts { get; }

    /// <summary>The preferred concept's taxonomy (first variant).</summary>
    public FactTaxonomy Taxonomy => Concepts[0].Taxonomy;

    /// <summary>The preferred concept's tag (first variant).</summary>
    public string Tag => Concepts[0].Tag;
}
