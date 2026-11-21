using Equibles.Core.Extensions;
using Equibles.CorporateActions.Repositories;
using Equibles.Sec.FinancialFacts.BusinessLogic.Splits;
using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Data.Models;
using Equibles.Sec.FinancialFacts.Data.Statements;
using Equibles.Sec.FinancialFacts.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Sec.FinancialFacts.BusinessLogic.ReleaseStatements;

/// <summary>
/// The one read of release figures every surface shares: the figures a company's periodic
/// reports do not cover yet, and their selection for one concept alias or one statement. A
/// surface shows them beside its own facts, marked as the release's; they never join the facts
/// a surface picks, derives or anchors on, since a release states part of a statement.
/// </summary>
public static class ReleaseFigureReader
{
    public static async Task<List<ReleaseStatementFigure>> Load(
        ReleaseStatementFigureRepository figures,
        FinancialFactRepository facts,
        StockSplitRepository splits,
        Guid issuerId,
        CancellationToken cancellationToken = default
    )
    {
        var uncovered = await figures.GetUncovered(issuerId, facts, cancellationToken);
        if (!uncovered.Any(MovesWithASplit))
            return uncovered;

        // A release states per-share figures and share counts on its own day's share basis. One
        // a later split moved is left out rather than restated: the periodic report restates it.
        var effective = await splits
            .GetEffectiveByStock(issuerId, DateOnly.FromDateTime(DateTime.UtcNow))
            .Select(split => split.EffectiveDate)
            .ToListAsync(cancellationToken);
        return uncovered
            .Where(figure =>
                !MovesWithASplit(figure) || !effective.Any(date => date > figure.FiledDate)
            )
            .ToList();
    }

    /// <summary>The alias's figure for each period, newest first; its first tag wins a period.</summary>
    public static List<ReleaseStatementFigure> ForAlias(
        IEnumerable<ReleaseStatementFigure> figures,
        string alias
    )
    {
        if (!FinancialConceptAliases.TryResolve(alias, out var concepts))
            return [];

        var rank = Rank(concepts);
        return figures
            .Where(figure => rank.ContainsKey(Key(figure)))
            .GroupBy(figure => (figure.PeriodStart, figure.PeriodEnd))
            .Select(period => period.OrderBy(figure => rank[Key(figure)]).First())
            .OrderByDescending(figure => figure.PeriodEnd)
            .ThenByDescending(figure => figure.PeriodStart)
            .ToList();
    }

    /// <summary>The statement's lines the release states, newest period first, in statement order.</summary>
    public static List<(StatementLine Line, ReleaseStatementFigure Figure)> ForStatement(
        IEnumerable<ReleaseStatementFigure> figures,
        FinancialStatementType statement
    )
    {
        var lines = FinancialStatementConcepts.For(statement);
        var rows = new List<(int Order, StatementLine Line, ReleaseStatementFigure Figure)>();
        var all = figures.ToList();
        for (var order = 0; order < lines.Count; order++)
        {
            var rank = Rank(lines[order].Concepts);
            rows.AddRange(
                all.Where(figure => rank.ContainsKey(Key(figure)))
                    .GroupBy(figure => (figure.PeriodStart, figure.PeriodEnd))
                    .Select(period =>
                        (order, lines[order], period.OrderBy(figure => rank[Key(figure)]).First())
                    )
            );
        }

        return rows.OrderByDescending(row => row.Figure.PeriodEnd)
            .ThenByDescending(row => row.Figure.PeriodStart)
            .ThenBy(row => row.Order)
            .Select(row => (row.Line, row.Figure))
            .ToList();
    }

    /// <summary>
    /// The period a figure covers, read from its span. Its stamp is the comparative fact's, and
    /// facts file a discrete fourth quarter under the full year and a year-to-date span under
    /// its quarter, so the stamp alone names two different spans.
    /// </summary>
    public static ReleaseFigurePeriod PeriodOf(ReleaseStatementFigure figure)
    {
        var stamp = figure.FiscalPeriod;
        if (figure.PeriodType == FactPeriodType.Instant)
            return new ReleaseFigurePeriod(stamp, YearToDate: false, Balance: true);

        var days = figure.PeriodEnd.DayNumber - figure.PeriodStart.DayNumber + 1;
        if (days >= FiscalPeriodSpanDays.MinAnnual)
            return new ReleaseFigurePeriod(SecFiscalPeriod.FullYear, false, false);
        if (days > FiscalPeriodSpanDays.MaxQuarter)
            return new ReleaseFigurePeriod(stamp, stamp != SecFiscalPeriod.FullYear, false);
        var fourthQuarter =
            stamp == SecFiscalPeriod.FullYear && days >= FiscalPeriodSpanDays.MinQuarter;
        return new ReleaseFigurePeriod(fourthQuarter ? SecFiscalPeriod.Q4 : stamp, false, false);
    }

    private static Dictionary<(FactTaxonomy, string), int> Rank(
        IReadOnlyList<FinancialConceptAliases.ConceptRef> concepts
    )
    {
        var rank = new Dictionary<(FactTaxonomy, string), int>();
        for (var i = 0; i < concepts.Count; i++)
            rank.TryAdd((concepts[i].Taxonomy, concepts[i].Tag), i);
        return rank;
    }

    private static (FactTaxonomy, string) Key(ReleaseStatementFigure figure) =>
        (figure.FinancialConcept.Taxonomy, figure.FinancialConcept.Tag);

    private static bool MovesWithASplit(ReleaseStatementFigure figure) =>
        FactSplitBasis.Classify(
            figure.FinancialConcept.Taxonomy,
            figure.FinancialConcept.Tag,
            figure.Unit
        ) != FactSplitKind.None;
}

/// <summary>A release figure's period: a quarter, the full year, or the year to date through a quarter.</summary>
public readonly record struct ReleaseFigurePeriod(
    SecFiscalPeriod Period,
    bool YearToDate,
    bool Balance
)
{
    public string Label => Period.NameForHumans() + (YearToDate ? " YTD" : "");

    /// <summary>
    /// Whether a request for one fiscal period asks for this figure. A year-to-date span answers
    /// no single period, and the year-end balance is also the fourth quarter's.
    /// </summary>
    public bool Answers(SecFiscalPeriod requested) =>
        !YearToDate
        && (
            Period == requested
            || (Balance && Period == SecFiscalPeriod.FullYear && requested == SecFiscalPeriod.Q4)
        );
}
