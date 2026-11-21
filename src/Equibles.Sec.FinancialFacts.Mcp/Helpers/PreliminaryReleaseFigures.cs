using System.Text;
using Equibles.CommonStocks.Data.Helpers;
using Equibles.CommonStocks.Data.Models;
using Equibles.Core.Extensions;
using Equibles.CorporateActions.Repositories;
using Equibles.Mcp.Helpers;
using Equibles.Sec.FinancialFacts.BusinessLogic.ReleaseStatements;
using Equibles.Sec.FinancialFacts.Data.Models;
using Equibles.Sec.FinancialFacts.Data.Statements;
using Equibles.Sec.FinancialFacts.Repositories;

namespace Equibles.Sec.FinancialFacts.Mcp.Helpers;

/// <summary>
/// The figures a company's earnings release states for periods no 10-Q or 10-K covers yet,
/// rendered as a block under a tool's own table. They never join the facts a tool picks,
/// derives or anchors on: a release states part of a statement, and only as the release words it.
/// </summary>
internal static class PreliminaryReleaseFigures
{
    internal const string Lead =
        "Preliminary, from the earnings release: no 10-Q or 10-K covers these periods yet. "
        + "They are the release's own figures, a later filing can state them differently, "
        + "and no calculated value uses them.";

    internal static Task<List<ReleaseStatementFigure>> Load(
        ReleaseStatementFigureRepository figures,
        FinancialFactRepository facts,
        StockSplitRepository splits,
        Guid issuerId
    ) =>
        figures == null
            ? Task.FromResult(new List<ReleaseStatementFigure>())
            : ReleaseFigureReader.Load(figures, facts, splits, issuerId);

    /// <summary>The block, or an empty string when there is no row; a null line omits the line column.</summary>
    internal static string Render(
        IReadOnlyList<(string Line, ReleaseStatementFigure Figure)> rows,
        EquityIssuer stock
    )
    {
        if (rows.Count == 0)
            return "";

        var withLine = rows.Any(row => row.Line != null);
        var result = new StringBuilder();
        result.AppendLine();
        result.AppendLine($"**{Lead}**");
        result.AppendLine();
        result.AppendLine(
            (withLine ? "| Line " : "")
                + "| Period Start | Period End | FY | Period | Value | Unit | Release row | Release filed | Document ID |"
        );
        result.AppendLine(
            (withLine ? "|------" : "")
                + "|--------------|------------|---:|--------|------:|------|-------------|---------------|-------------|"
        );
        foreach (var (line, figure) in rows)
            result.AppendLine(
                (withLine ? $"| {FactMarkdown.Cell(line)} " : "")
                    + $"| {figure.PeriodStart:yyyy-MM-dd} | {figure.PeriodEnd:yyyy-MM-dd} "
                    + $"| {FiscalYearLabel.For(figure.FiscalYear, stock)} "
                    + $"| {figure.FiscalPeriod.NameForHumans()} "
                    + $"| {FactMarkdown.Value(figure.Value, figure.Unit)} "
                    + $"| {FactMarkdown.Cell(figure.Unit)} "
                    + $"| {FactMarkdown.Cell(figure.SourceLabel)} (line {figure.SourceLine}) "
                    + $"| {figure.FiledDate:yyyy-MM-dd} "
                    + $"| {figure.DocumentId} |"
            );
        return result.ToString();
    }
}
