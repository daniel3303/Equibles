using System.ComponentModel;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.CommonStocks.Repositories.Extensions;
using Equibles.Core.Extensions;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.BusinessLogic.Extensions;
using Equibles.Errors.Data.Models;
using Equibles.Mcp;
using Equibles.Mcp.Helpers;
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
        _runner = new McpToolRunner(logger, errorManager.AsMcpErrorReporter());
    }

    [McpServerTool(Name = "GetFinancialStatement", Title = "Financial Statements", ReadOnly = true)]
    [Description(
        "Get a company's income statement, balance sheet, or cash-flow statement for a "
            + "given fiscal year and period, sourced from SEC Company Facts (structured XBRL). "
            + "Returns the standard line items (e.g. revenue, net income, total assets, "
            + "operating cash flow) with the latest-restated value for the period. "
            + "Company-specific dimensional facts (e.g. product-segment revenue) are not "
            + "included — use GetRevenueBreakdown for segment/geographic revenue, and "
            + "GetFinancialFact or CompareFinancialFact for one line item across periods "
            + "or across companies."
    )]
    public Task<string> GetFinancialStatement(
        [Description("Stock ticker symbol (e.g., AAPL, MSFT, GME)")] string ticker,
        [Description(
            "Statement: 'income' (income statement), 'balance' (balance sheet), or "
                + "'cashflow' (cash-flow statement); the aliases 'is'/'p&l', 'bs' and 'cf' "
                + "also work. Defaults to income."
        )]
            string statement = "income",
        [Description("Fiscal year, e.g. 2023. Defaults to the latest reported year.")]
            int? year = null,
        [Description(
            "Fiscal period: 'FY' (annual) or 'Q1'..'Q4'. Defaults to the latest reported "
                + "period. Most filers report no discrete Q4 income/cash-flow facts in XBRL "
                + "(the fourth quarter is embedded in the full-year figure) — use 'FY' for "
                + "annual figures."
        )]
            string period = null
    )
    {
        return _runner.Execute(
            async () =>
            {
                if (string.IsNullOrWhiteSpace(ticker))
                    return "A ticker symbol is required.";

                var (stock, stockError) = await _commonStockRepository.ResolveByTicker(ticker);
                if (stockError != null)
                    return stockError;

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

                var statementLines = FinancialStatementConcepts.For(statementType);
                var (taxonomies, tags) = StatementLineFacts.CollectConceptPairs(statementLines);

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

                // Period availability is scoped to the requested statement's
                // concepts: a (year, Q4) that only exists via balance-sheet
                // instants must not validate an income-statement request and
                // then render an empty table.
                var (selectedYear, selectedPeriod, periodError) = await ResolveStatementPeriod(
                    stock,
                    statementType,
                    year,
                    requestedPeriod,
                    conceptIds
                );
                if (periodError != null)
                    return periodError;

                var facts = await _financialFactRepository
                    .GetConsolidatedByStock(stock)
                    .Where(f =>
                        f.FiscalYear == selectedYear
                        && f.FiscalPeriod == selectedPeriod
                        && conceptIds.Contains(f.FinancialConceptId)
                    )
                    .ToListAsync();

                // A 10-Q tags each line under one fiscal (year, period) for both the
                // discrete quarter and the fiscal year-to-date span, and re-reports prior
                // comparative instants (the prior fiscal-year-end balance) under that same
                // identity. Picking by filed date alone surfaces the year-to-date as the
                // quarter and mixes the comparative balance-sheet columns, so the statement
                // stops balancing (#1546). Prefer the span matching the period's granularity,
                // then the instant ending latest, then the latest restatement — the same
                // selection the web surfaces apply in
                // FinancialStatementsHelper.PickCurrentlyReportedFact.
                var latestByConcept = facts
                    .GroupBy(f => f.FinancialConceptId)
                    .ToDictionary(g => g.Key, g => PickCurrentlyReportedFact(g, selectedPeriod));

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
        var result = MarkdownTable.Start(
            $"{statementType.NameForHumans()} for {stock.Ticker} "
                + $"({FactMarkdown.Cell(stock.Name)}) — "
                + $"FY{selectedYear} {selectedPeriod.NameForHumans()}:",
            "| Line Item | Value | Unit | Period End | Form | Filed |",
            "|-----------|------:|------|-----------|------|-------|"
        );

        var rendered = 0;
        var omitted = 0;
        DateOnly? earliestFiled = null;
        DateOnly? latestFiled = null;
        foreach (var line in statementLines)
        {
            var fact = StatementLineFacts.PickFact(line, conceptIdByKey, latestByConcept);
            if (fact == null)
            {
                // The template carries industry-specific lines (bank/insurer/
                // REIT top lines) most filers never report — dash-only rows
                // are noise, so unreported lines are omitted and counted.
                omitted++;
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
            if (earliestFiled == null || fact.FiledDate < earliestFiled)
                earliestFiled = fact.FiledDate;
            if (latestFiled == null || fact.FiledDate > latestFiled)
                latestFiled = fact.FiledDate;
        }

        if (rendered == 0)
            return $"No {statementType.NameForHumans().ToLowerInvariant()} line items were "
                + $"reported by {stock.Ticker} for FY{selectedYear} "
                + $"{selectedPeriod.NameForHumans()}.";

        if (omitted > 0)
            result.AppendLine(
                "\n_Line items the filer did not report for this period are omitted._"
            );

        // Each line independently takes its latest restatement, so one
        // statement can mix filing vintages — flag it so a partially restated
        // total that disagrees with non-restated components is explainable.
        if (earliestFiled != latestFiled)
            result.AppendLine(
                $"\n_Values reflect the latest restatement per line; source filings span "
                    + $"{earliestFiled:yyyy-MM-dd} to {latestFiled:yyyy-MM-dd} — see the Filed column._"
            );

        return result.ToString();
    }

    private async Task<(
        int FiscalYear,
        SecFiscalPeriod FiscalPeriod,
        string Error
    )> ResolveStatementPeriod(
        CommonStock stock,
        FinancialStatementType statementType,
        int? year,
        SecFiscalPeriod? requestedPeriod,
        IReadOnlySet<Guid> statementConceptIds
    )
    {
        var statementName = statementType.NameForHumans().ToLowerInvariant();
        var availablePeriods = await _financialFactRepository
            .GetConsolidatedByStock(stock)
            .Where(f => statementConceptIds.Contains(f.FinancialConceptId))
            .Select(f => new { f.FiscalYear, f.FiscalPeriod })
            .Distinct()
            .ToListAsync();

        if (availablePeriods.Count == 0)
        {
            // Distinguish "nothing ingested at all" from "nothing for THIS
            // statement" so the caller isn't told a covered company is absent.
            var hasAnyFacts = await _financialFactRepository
                .GetConsolidatedByStock(stock)
                .AnyAsync();
            return (
                default,
                default,
                hasAnyFacts
                    ? $"No {statementName} line items have been ingested for {stock.Ticker}."
                    : $"No structured financial facts have been ingested for {stock.Ticker}."
            );
        }

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
            var message =
                $"{stock.Ticker} has no {statementName} data for {wanted}. Latest available: "
                + $"FY{latest.FiscalYear} {latest.FiscalPeriod.NameForHumans()}.";
            // The Q4-under-FY trap: SEC Company Facts embeds the fourth
            // quarter's flow facts in the full-year duration, so a Q4
            // income/cash-flow request usually has nothing to find — that is
            // an XBRL filing convention, not a gap in the company's reporting.
            if (
                requestedPeriod == SecFiscalPeriod.Q4
                && statementType != FinancialStatementType.BalanceSheet
            )
                message +=
                    " Most filers report no discrete fourth-quarter flow facts in XBRL — "
                    + "the fourth quarter is embedded in the full-year figure. Use period "
                    + "'FY' for annual figures, or GetFinancialFact for a single concept "
                    + "(it surfaces reported discrete Q4 rows where they exist).";
            return (default, default, message);
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

    // The longest span a discrete fiscal quarter can cover — 13 weeks on a 4-4-5
    // calendar plus the occasional 14-week quarter, with headroom.
    private const int MaxDiscreteQuarterDays = 100;

    // The shortest span a fiscal year can cover — 52 weeks on a 52/53-week
    // calendar, with headroom for short transition years.
    private const int MinAnnualSpanDays = 350;

    // The currently-reported fact for one concept within a single fiscal (year, period).
    // A 10-Q reports each flow line twice under that identity — the discrete quarter and
    // the fiscal year-to-date — and a balance-sheet line carries the period-end instant
    // alongside re-stated comparative instants from prior filings. Prefer the span that
    // matches the period's granularity (instants span zero days and always qualify), then
    // the instant ending latest so a comparative column never stands in for the current
    // one, then the latest restatement among same-ending candidates (#1546). Mirrors
    // FinancialStatementsHelper.PickCurrentlyReportedFact on the web surfaces; facts are
    // already consolidated-only here via GetConsolidatedByStock.
    internal static FinancialFact PickCurrentlyReportedFact(
        IEnumerable<FinancialFact> facts,
        SecFiscalPeriod fiscalPeriod
    ) => StatementLineFacts.PickCurrentlyReported(facts, fiscalPeriod);

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
