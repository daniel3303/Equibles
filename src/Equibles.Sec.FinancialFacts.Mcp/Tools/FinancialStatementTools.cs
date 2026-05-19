using System.ComponentModel;
using System.Globalization;
using System.Text;
using Equibles.CommonStocks.Repositories;
using Equibles.Core.Extensions;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Equibles.Mcp;
using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Data.Statements;
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
    private readonly ErrorManager _errorManager;
    private readonly ILogger<FinancialStatementTools> _logger;

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
        _errorManager = errorManager;
        _logger = logger;
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
        return McpToolExecutor.Execute(
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
                    if (!TryParsePeriod(period, out var parsedPeriod))
                        return $"Unknown period '{period}'. Use 'FY' or 'Q1'..'Q4'.";
                    requestedPeriod = parsedPeriod;
                }

                var availablePeriods = await _financialFactRepository
                    .GetByStock(stock)
                    .Select(f => new { f.FiscalYear, f.FiscalPeriod })
                    .Distinct()
                    .ToListAsync();

                if (availablePeriods.Count == 0)
                    return $"No structured financial facts have been ingested for {stock.Ticker}.";

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
                    return $"{stock.Ticker} has no data for {wanted}. Latest available: "
                        + $"FY{latest.FiscalYear} {latest.FiscalPeriod.NameForHumans()}.";
                }

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
                        f.FiscalYear == selected.FiscalYear
                        && f.FiscalPeriod == selected.FiscalPeriod
                        && conceptIds.Contains(f.FinancialConceptId)
                    )
                    .ToListAsync();

                // Restatements re-emit the same concept; the latest-filed value
                // is the currently-reported one.
                var latestByConcept = facts
                    .GroupBy(f => f.FinancialConceptId)
                    .ToDictionary(g => g.Key, g => g.OrderByDescending(f => f.FiledDate).First());

                var result = new StringBuilder();
                result.AppendLine(
                    $"{statementType.NameForHumans()} for {stock.Ticker} ({Md(stock.Name)}) — "
                        + $"FY{selected.FiscalYear} {selected.FiscalPeriod.NameForHumans()}:"
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
                        result.AppendLine($"| {Md(line.Label)} | — | | | | |");
                        continue;
                    }

                    var isPerShare =
                        fact.Unit != null
                        && fact.Unit.EndsWith("/shares", StringComparison.OrdinalIgnoreCase);
                    // Format culture-invariantly so the LLM-facing output is
                    // identical regardless of the server's locale.
                    var number = fact.Value.ToString(
                        isPerShare ? "N2" : "N0",
                        CultureInfo.InvariantCulture
                    );
                    // Only USD gets a "$"; other currencies (EUR/GBP for 20-F /
                    // 40-F filers) are conveyed by the Unit column rather than
                    // mislabelled with a dollar sign.
                    var isUsd =
                        fact.Unit != null
                        && fact.Unit.StartsWith("USD", StringComparison.OrdinalIgnoreCase);
                    var value = isUsd ? "$" + number : number;

                    result.AppendLine(
                        $"| {Md(line.Label)} | {value} | {Md(fact.Unit)} | "
                            + $"{fact.PeriodEnd:yyyy-MM-dd} | {Md(fact.Form?.DisplayName)} | "
                            + $"{fact.FiledDate:yyyy-MM-dd} |"
                    );
                    rendered++;
                }

                if (rendered == 0)
                    result.AppendLine(
                        $"\n_No line items of this statement were reported for the period._"
                    );

                return result.ToString();
            },
            _logger,
            "GetFinancialStatement",
            $"ticker: {Clean(ticker)}, statement: {Clean(statement)}, "
                + $"year: {year}, period: {Clean(period)}",
            ReportError
        );
    }

    // Escapes the Markdown table delimiters so a value containing '|' or a
    // newline (e.g. some ADR/fund names) can't break the table the LLM consumes.
    private static string Md(string value) =>
        value == null ? "" : value.Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");

    // Strips control chars from untrusted args before they reach logs / the
    // error store, matching the repo's existing log-hygiene precedent (e91e72a).
    private static string Clean(string value) =>
        value == null ? "" : new string(value.Where(c => !char.IsControl(c)).ToArray());

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

    private static bool TryParsePeriod(string value, out SecFiscalPeriod period)
    {
        switch (value?.Trim().ToUpperInvariant())
        {
            case "FY":
            case "FULLYEAR":
            case "ANNUAL":
                period = SecFiscalPeriod.FullYear;
                return true;
            case "Q1":
                period = SecFiscalPeriod.Q1;
                return true;
            case "Q2":
                period = SecFiscalPeriod.Q2;
                return true;
            case "Q3":
                period = SecFiscalPeriod.Q3;
                return true;
            case "Q4":
                period = SecFiscalPeriod.Q4;
                return true;
            default:
                period = default;
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

    private Task ReportError(string toolName, string message, string stackTrace, string context)
    {
        return _errorManager.Create(ErrorSource.McpTool, toolName, message, stackTrace, context);
    }
}
