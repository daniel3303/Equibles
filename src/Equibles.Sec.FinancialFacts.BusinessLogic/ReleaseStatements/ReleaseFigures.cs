using Equibles.Sec.FinancialFacts.Data.Enums;

namespace Equibles.Sec.FinancialFacts.BusinessLogic.ReleaseStatements;

/// <summary>A consolidated periodic-report fact the issuer had on file before the release.</summary>
public sealed record OnFileFact(
    Guid ConceptId,
    FactTaxonomy Taxonomy,
    string Tag,
    string Unit,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    decimal Value,
    int FiscalYear,
    SecFiscalPeriod FiscalPeriod,
    ConceptBalance? Balance
);

/// <summary>
/// One line of a primary statement in a periodic report on file: the label the filer gave the
/// concept on the statement's face.
/// </summary>
public sealed record FiledStatementLine(FactTaxonomy Taxonomy, string Tag, string Label)
{
    /// <summary>
    /// A statement line as the filing tagged it. A filer's own tag is stored with its prefix
    /// under the company-specific taxonomy, as its facts are.
    /// </summary>
    public static FiledStatementLine From(string taxonomyPrefix, string concept, string label)
    {
        FactTaxonomy? standard = taxonomyPrefix?.ToLowerInvariant() switch
        {
            "us-gaap" => FactTaxonomy.UsGaap,
            "dei" => FactTaxonomy.Dei,
            "ifrs-full" => FactTaxonomy.IfrsFull,
            "srt" => FactTaxonomy.Srt,
            "invest" => FactTaxonomy.Invest,
            _ => null,
        };
        return standard is { } taxonomy
            ? new FiledStatementLine(taxonomy, concept, label)
            : new FiledStatementLine(FactTaxonomy.Custom, $"{taxonomyPrefix}:{concept}", label);
    }
}

/// <summary>
/// One figure an earnings release states for a period no periodic report covers yet, placed on
/// a concept because the same row's comparative value equals that concept's fact on file.
/// </summary>
public sealed record ReleaseFigure(
    Guid ConceptId,
    string Unit,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    decimal Value,
    int FiscalYear,
    SecFiscalPeriod FiscalPeriod,
    int SourceLine,
    string SourceLabel
)
{
    public FactPeriodType PeriodType =>
        PeriodStart == PeriodEnd ? FactPeriodType.Instant : FactPeriodType.Duration;
}
