using System.ComponentModel;
using System.Text;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Core.Extensions;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Equibles.Mcp;
using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Data.Models;
using Equibles.Sec.FinancialFacts.Data.Statements;
using Equibles.Sec.FinancialFacts.Mcp.Helpers;
using Equibles.Sec.FinancialFacts.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace Equibles.Sec.FinancialFacts.Mcp.Tools;

[McpServerToolType]
public class FinancialStatementTools
{
    private readonly FinancialFactRepository _financialFactRepository;
    private readonly FinancialConceptRepository _financialConceptRepository;
    private readonly CommonStockRepository _commonStockRepository;
    private readonly McpToolRunner _runner;

    public FinancialStatementTools(
        FinancialFactRepository financialFactRepository,
        FinancialConceptRepository financialConceptRepository,
        CommonStockRepository commonStockRepository,
        ErrorManager errorManager,
        ILogger<FinancialStatementTools> logger
    )
    {
        _financialFactRepository = financialFactRepository;
        _financialConceptRepository = financialConceptRepository;
        _commonStockRepository = commonStockRepository;
        _runner = new McpToolRunner(
            logger,
            (tool, msg, stack, ctx) =>
                errorManager.Create(ErrorSource.McpTool, tool, msg, stack, ctx)
        );
    }

    [McpServerTool(Name = "GetFinancialStatement")]
    [Description(
        "Get a company's income statement, balance sheet, or cash-flow statement for a "
            + "given fiscal year and period, sourced from SEC Company Facts (structured XBRL). "
            + "Returns the standard line items (e.g. revenue, net income, total assets, "
            + "operating cash flow) with the latest-restated value for the period. "
            + "Company-specific dimensional facts (e.g. product-segment revenue) are not included."
    )]
    public Task<string> GetFinancialStatement(
        [Description("Stock ticker symbol (e.g., AAPL, MSFT, GME)")] string ticker,
        [Description(
            "Statement: 'income' (income statement), 'balance' (balance sheet), or "
                + "'cashflow' (cash-flow statement). Defaults to income."
        )]
            string statement = "income",
        [Description("Fiscal year, e.g. 2023. Defaults to the latest reported year.")]
            int? year = null,
        [Description(
            "Fiscal period: 'FY' (annual) or 'Q1'..'Q4'. Defaults to the latest reported period."
        )]
            string period = null
    )
    {
        return _runner.Execute(
            async () =>
            {
                if (string.IsNullOrWhiteSpace(ticker))
                    return "A ticker symbol is required.";

                var stock = await _commonStockRepository.GetByTicker(
                    ticker.Trim().ToUpperInvariant()
                );
                if (stock == null)
                    return $"Stock '{ticker}' not found.";

                if (!TryParseStatement(statement, out var statementType))
                    return $"Unknown statement '{statement}'. Use 'income', 'balance', or 'cashflow'.";

                // Parse the period once into a nullable; null means "no explicit
                // period requested" (default to latest), a value means the caller
                // asked for that exact period.
                SecFiscalPeriod? requestedPeriod = null;
                if (period != null)
                {
                    if (!FactArgs.TryParsePeriod(period, out var parsedPeriod))
                        return $"Unknown period '{period}'. Use 'FY' or 'Q1'..'Q4'.";
                    requestedPeriod = parsedPeriod;
                }

                var (selectedYear, selectedPeriod, periodError) = await ResolveStatementPeriod(
                    stock,
                    year,
                    requestedPeriod
                );
                if (periodError != null)
                    return periodError;

                var statementLines = FinancialStatementConcepts.For(statementType);
                var taxonomies = statementLines.Select(l => l.Taxonomy).Distinct().ToList();
                var tags = statementLines.Select(l => l.Tag).Distinct().ToList();

                var concepts = await _financialConceptRepository
                    .GetMatching(taxonomies, tags)
                    .Select(c => new
                    {
                        c.Id,
                        c.Taxonomy,
                        c.Tag,
                    })
                    .ToListAsync();
                var conceptIdByKey = concepts.ToDictionary(c => (c.Taxonomy, c.Tag), c => c.Id);
                var conceptIds = concepts.Select(c => c.Id).ToHashSet();

                var facts = await _financialFactRepository
                    .GetByStock(stock)
                    .Where(f =>
                        f.FiscalYear == selectedYear
                        && f.FiscalPeriod == selectedPeriod
                        && conceptIds.Contains(f.FinancialConceptId)
                    )
                    .ToListAsync();

                // Restatements re-emit the same concept; the latest-filed value
                // is the currently-reported one.
                var latestByConcept = facts
                    .GroupBy(f => f.FinancialConceptId)
                    .ToDictionary(g => g.Key, g => g.OrderByDescending(f => f.FiledDate).First());

                return RenderStatementTable(
                    stock,
                    statementType,
                    selectedYear,
                    selectedPeriod,
                    statementLines,
                    conceptIdByKey,
                    latestByConcept
                );
            },
            "GetFinancialStatement",
            $"ticker: {FactMarkdown.Clean(ticker)}, statement: {FactMarkdown.Clean(statement)}, "
                + $"year: {year}, period: {FactMarkdown.Clean(period)}"
        );
    }

    private static string RenderStatementTable(
        CommonStock stock,
        FinancialStatementType statementType,
        int selectedYear,
        SecFiscalPeriod selectedPeriod,
        IReadOnlyList<StatementLine> statementLines,
        Dictionary<(FactTaxonomy Taxonomy, string Tag), Guid> conceptIdByKey,
        Dictionary<Guid, FinancialFact> latestByConcept
    )
    {
        var result = new StringBuilder();
        result.AppendLine(
            $"{statementType.NameForHumans()} for {stock.Ticker} "
                + $"({FactMarkdown.Cell(stock.Name)}) — "
                + $"FY{selectedYear} {selectedPeriod.NameForHumans()}:"
        );
        result.AppendLine();
        result.AppendLine("| Line Item | Value | Unit | Period End | Form | Filed |");
        result.AppendLine("|-----------|------:|------|-----------|------|-------|");

        var rendered = 0;
        foreach (var line in statementLines)
        {
            if (
                !conceptIdByKey.TryGetValue((line.Taxonomy, line.Tag), out var conceptId)
                || !latestByConcept.TryGetValue(conceptId, out var fact)
            )
            {
                result.AppendLine($"| {FactMarkdown.Cell(line.Label)} | — | | | | |");
                continue;
            }

            result.AppendLine(
                $"| {FactMarkdown.Cell(line.Label)} | "
                    + $"{FactMarkdown.Value(fact.Value, fact.Unit)} | "
                    + $"{FactMarkdown.Cell(fact.Unit)} | "
                    + $"{fact.PeriodEnd:yyyy-MM-dd} | "
                    + $"{FactMarkdown.Cell(fact.Form?.DisplayName)} | "
                    + $"{fact.FiledDate:yyyy-MM-dd} |"
            );
            rendered++;
        }

        if (rendered == 0)
            result.AppendLine($"\n_No line items of this statement were reported for the period._");

        return result.ToString();
    }

    private async Task<(
        int FiscalYear,
        SecFiscalPeriod FiscalPeriod,
        string Error
    )> ResolveStatementPeriod(CommonStock stock, int? year, SecFiscalPeriod? requestedPeriod)
    {
        var availablePeriods = await _financialFactRepository
            .GetByStock(stock)
            .Select(f => new { f.FiscalYear, f.FiscalPeriod })
            .Distinct()
            .ToListAsync();

        if (availablePeriods.Count == 0)
            return (
                default,
                default,
                $"No structured financial facts have been ingested for {stock.Ticker}."
            );

        var ordered = availablePeriods
            .OrderByDescending(p => p.FiscalYear)
            .ThenByDescending(p => ChronologicalRank(p.FiscalPeriod))
            .ToList();

        // An explicit year/period must match exactly — never silently
        // substitute a different period's figures for financial data.
        // When neither is given, the first (chronologically latest) wins.
        var selected = ordered.FirstOrDefault(p =>
            (year == null || p.FiscalYear == year)
            && (requestedPeriod == null || p.FiscalPeriod == requestedPeriod.Value)
        );

        if (selected == null)
        {
            var latest = ordered[0];
            var wanted =
                $"{(year?.ToString() ?? "the latest year")} "
                + $"{(requestedPeriod?.NameForHumans() ?? "period")}";
            return (
                default,
                default,
                $"{stock.Ticker} has no data for {wanted}. Latest available: "
                    + $"FY{latest.FiscalYear} {latest.FiscalPeriod.NameForHumans()}."
            );
        }

        return (selected.FiscalYear, selected.FiscalPeriod, null);
    }

    private static bool TryParseStatement(string value, out FinancialStatementType type)
    {
        switch (value?.Trim().ToLowerInvariant())
        {
            case "income":
            case "income-statement":
            case "incomestatement":
            case "is":
            case "p&l":
            case "pnl":
                type = FinancialStatementType.IncomeStatement;
                return true;
            case "balance":
            case "balance-sheet":
            case "balancesheet":
            case "bs":
                type = FinancialStatementType.BalanceSheet;
                return true;
            case "cashflow":
            case "cash-flow":
            case "cash flow":
            case "cf":
                type = FinancialStatementType.CashFlow;
                return true;
            default:
                type = default;
                return false;
        }
    }

    // Chronological order within a fiscal year: Q1 < Q2 < Q3 < Q4 < FullYear.
    private static int ChronologicalRank(SecFiscalPeriod period) =>
        period switch
        {
            SecFiscalPeriod.Q1 => 1,
            SecFiscalPeriod.Q2 => 2,
            SecFiscalPeriod.Q3 => 3,
            SecFiscalPeriod.Q4 => 4,
            SecFiscalPeriod.FullYear => 5,
            _ => 0,
        };
}
