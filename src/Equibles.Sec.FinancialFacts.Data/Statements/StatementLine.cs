using Equibles.Sec.FinancialFacts.Data.Enums;

namespace Equibles.Sec.FinancialFacts.Data.Statements;

/// <summary>
/// One line of a financial statement: the XBRL concept to pull
/// (<see cref="Taxonomy"/> + <see cref="Tag"/>) and the human-readable
/// <see cref="Label"/> to render for it. Ordering within a statement is the
/// order these are declared in <see cref="FinancialStatementConcepts"/>.
/// </summary>
public class StatementLine
{
    public StatementLine(FactTaxonomy taxonomy, string tag, string label)
    {
        Taxonomy = taxonomy;
        Tag = tag;
        Label = label;
    }

    public FactTaxonomy Taxonomy { get; }

    public string Tag { get; }

    public string Label { get; }
}
