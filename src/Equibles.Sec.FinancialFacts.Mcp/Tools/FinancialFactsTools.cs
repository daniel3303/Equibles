using System.ComponentModel;
using System.Globalization;
using System.Text;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Core.Extensions;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Equibles.Mcp;
using Equibles.Sec.Data.Models;
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
public class FinancialFactsTools
{
    private const int MaxResultsCap = 200;
    private const int MaxTickers = 25;

    private readonly FinancialFactRepository _financialFactRepository;
    private readonly FinancialConceptRepository _financialConceptRepository;
    private readonly CommonStockRepository _commonStockRepository;
    private readonly ErrorManager _errorManager;
    private readonly ILogger<FinancialFactsTools> _logger;

    public FinancialFactsTools(
        FinancialFactRepository financialFactRepository,
        FinancialConceptRepository financialConceptRepository,
        CommonStockRepository commonStockRepository,
        ErrorManager errorManager,
        ILogger<FinancialFactsTools> logger
    )
    {
        _financialFactRepository = financialFactRepository;
        _financialConceptRepository = financialConceptRepository;
        _commonStockRepository = commonStockRepository;
        _errorManager = errorManager;
        _logger = logger;
    }

    [McpServerTool(Name = "GetFinancialFact")]
    [Description(
        "Get a single financial concept (e.g. revenue, net income, diluted EPS, total "
            + "assets, operating cash flow) over time for a company, sourced from SEC "
            + "Company Facts (structured XBRL). Returns a time series, one row per fiscal "
            + "period, using the latest restated value unless asOriginallyReported is set."
    )]
    public Task<string> GetFinancialFact(
        [Description("Stock ticker symbol (e.g., AAPL, MSFT)")] string ticker,
        [Description(
            "Concept alias, e.g. 'revenue', 'net-income', 'eps-diluted', 'total-assets', "
                + "'operating-cash-flow'. Call with an unknown value to list supported aliases."
        )]
            string concept,
        [Description("Optional SEC form filter, e.g. '10-K' or '10-Q'")] string form = null,
        [Description("Optional earliest period-end date, YYYY-MM-DD")] string fromDate = null,
        [Description("Optional latest period-end date, YYYY-MM-DD")] string toDate = null,
        [Description("Maximum periods to return, newest first (default 40, max 200)")]
            int maxResults = 40,
        [Description(
            "When true, show the value as originally filed (earliest filing) instead of "
                + "the latest restatement. Default false."
        )]
            bool asOriginallyReported = false
    )
    {
        return Execute(
            async () =>
            {
                if (string.IsNullOrWhiteSpace(ticker))
                    return "A ticker symbol is required.";

                if (string.IsNullOrWhiteSpace(concept))
                    return $"A concept is required. {SupportedAliasesNote()}";

                var stock = await _commonStockRepository.GetByTicker(
                    ticker.Trim().ToUpperInvariant()
                );
                if (stock == null)
                    return $"Stock '{ticker}' not found.";

                if (!FinancialConceptAliases.TryResolve(concept, out var conceptRefs))
                    return $"Unknown concept '{concept}'. {SupportedAliasesNote()}";

                DocumentType formFilter = null;
                if (!string.IsNullOrWhiteSpace(form))
                {
                    formFilter = DocumentType.FromDisplayName(form.Trim());
                    if (formFilter == null)
                        return $"Unknown form '{form}'. Use e.g. '10-K' or '10-Q'.";
                }

                if (!TryParseBound(fromDate, out var fromBound))
                    return $"Unknown date '{fromDate}'. Use YYYY-MM-DD.";
                if (!TryParseBound(toDate, out var toBound))
                    return $"Unknown date '{toDate}'. Use YYYY-MM-DD.";

                // conceptId → alias priority (0 = the alias's primary tag). Used
                // to break ties deterministically when one period carries facts
                // under several of the alias's tags (the ASC 606 transition).
                var conceptPriority = await ResolveConceptPriority(conceptRefs);
                if (conceptPriority.Count == 0)
                    return $"No '{concept}' data has been ingested for {stock.Ticker}.";

                var facts = await _financialFactRepository
                    .GetByStock(stock)
                    .Where(f => conceptPriority.Keys.Contains(f.FinancialConceptId))
                    .ToListAsync();

                // Form / period-end filtering in memory: a single concept's
                // history is small, and DocumentType is a value-converted type
                // so equality is kept off the SQL side for provider safety.
                var filtered = facts.Where(f =>
                    (formFilter == null || f.Form == formFilter)
                    && (fromBound == null || f.PeriodEnd >= fromBound.Value)
                    && (toBound == null || f.PeriodEnd <= toBound.Value)
                );

                // One row per fiscal period. The alias may span several tags and
                // a single period can carry facts under more than one of them
                // (the ASC 606 transition year tags both Revenues and the new
                // concept). Pick deterministically: the alias's primary tag
                // wins, then the latest filing — or the earliest, when the
                // caller wants the figure as originally reported.
                var perPeriod = filtered
                    .GroupBy(f => (f.FiscalYear, f.FiscalPeriod))
                    .Select(g => PickBestFact(g, conceptPriority, asOriginallyReported))
                    .OrderByDescending(f => f.PeriodEnd)
                    .Take(Math.Clamp(maxResults, 1, MaxResultsCap))
                    .ToList();

                if (perPeriod.Count == 0)
                    return $"No '{concept}' data found for {stock.Ticker} with the given filters.";

                return RenderFactHistoryTable(concept, stock, asOriginallyReported, perPeriod);
            },
            "GetFinancialFact",
            $"ticker: {FactMarkdown.Clean(ticker)}, concept: {FactMarkdown.Clean(concept)}, "
                + $"form: {FactMarkdown.Clean(form)}"
        );
    }

    [McpServerTool(Name = "CompareFinancialFact")]
    [Description(
        "Compare one financial concept across several companies for the same fiscal "
            + "period — peer comparison. Returns one row per ticker with the "
            + "latest-restated value; tickers with no data for the period are listed "
            + "separately."
    )]
    public Task<string> CompareFinancialFact(
        [Description("Comma-separated tickers, e.g. 'AAPL,MSFT,GOOGL' (max 25)")] string tickers,
        [Description(
            "Concept alias, e.g. 'revenue', 'net-income', 'eps-diluted'. Call with an "
                + "unknown value to list supported aliases."
        )]
            string concept,
        [Description("Fiscal year, e.g. 2023")] int fiscalYear,
        [Description("Fiscal period: 'FY' (default) or 'Q1'..'Q4'")] string fiscalPeriod = "FY"
    )
    {
        return Execute(
            async () =>
            {
                if (string.IsNullOrWhiteSpace(concept))
                    return $"A concept is required. {SupportedAliasesNote()}";

                if (!FinancialConceptAliases.TryResolve(concept, out var conceptRefs))
                    return $"Unknown concept '{concept}'. {SupportedAliasesNote()}";

                if (!FactArgs.TryParsePeriod(fiscalPeriod, out var period))
                    return $"Unknown period '{fiscalPeriod}'. Use 'FY' or 'Q1'..'Q4'.";

                if (string.IsNullOrWhiteSpace(tickers))
                    return "At least one ticker is required.";

                var requested = tickers
                    .Split(
                        ',',
                        StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
                    )
                    .Select(t => t.ToUpperInvariant())
                    .Distinct()
                    .ToList();

                if (requested.Count == 0)
                    return "At least one ticker is required.";
                if (requested.Count > MaxTickers)
                    return $"Too many tickers ({requested.Count}). The maximum is {MaxTickers}.";

                var conceptPriority = await ResolveConceptPriority(conceptRefs);
                if (conceptPriority.Count == 0)
                    return $"No '{concept}' data has been ingested.";

                // Two queries instead of 2N: batch-load the requested stocks,
                // then all matching facts in one go keyed by company.
                var stocks = await _commonStockRepository.GetByTickers(requested).ToListAsync();
                var stockByTicker = new Dictionary<string, CommonStock>();
                foreach (var s in stocks)
                {
                    if (requested.Contains(s.Ticker))
                        stockByTicker.TryAdd(s.Ticker, s);
                    foreach (var secondary in s.SecondaryTickers ?? [])
                        if (requested.Contains(secondary))
                            stockByTicker.TryAdd(secondary, s);
                }

                var stockIds = stocks.Select(s => s.Id).ToList();
                var facts = await _financialFactRepository
                    .GetByStocks(stockIds)
                    .Where(f =>
                        f.FiscalYear == fiscalYear
                        && f.FiscalPeriod == period
                        && conceptPriority.Keys.Contains(f.FinancialConceptId)
                    )
                    .ToListAsync();

                // Same deterministic pick as GetFinancialFact: the alias's
                // primary tag wins, then the latest filing, then accession as a
                // stable tiebreak for same-day amendments (Postgres has no
                // implicit row order).
                var bestByStock = facts
                    .GroupBy(f => f.CommonStockId)
                    .ToDictionary(g => g.Key, g => PickBestFact(g, conceptPriority));

                var rows = new List<(string Ticker, string Name, FinancialFact Fact)>();
                var skipped = new List<string>();
                foreach (var ticker in requested)
                {
                    if (!stockByTicker.TryGetValue(ticker, out var stock))
                    {
                        skipped.Add($"{FactMarkdown.Cell(ticker)} (not found)");
                        continue;
                    }
                    if (!bestByStock.TryGetValue(stock.Id, out var best))
                    {
                        skipped.Add($"{FactMarkdown.Cell(ticker)} (no data)");
                        continue;
                    }
                    rows.Add((stock.Ticker, stock.Name, best));
                }

                return RenderComparisonTable(concept, fiscalYear, period, rows, skipped);
            },
            "CompareFinancialFact",
            $"tickers: {FactMarkdown.Clean(tickers)}, concept: {FactMarkdown.Clean(concept)}, "
                + $"year: {fiscalYear}, period: {FactMarkdown.Clean(fiscalPeriod)}"
        );
    }

    private static string RenderFactHistoryTable(
        string concept,
        CommonStock stock,
        bool asOriginallyReported,
        List<FinancialFact> perPeriod
    )
    {
        var basis = asOriginallyReported ? "as originally reported" : "latest restated";
        var result = new StringBuilder();
        result.AppendLine(
            $"{concept} for {stock.Ticker} ({FactMarkdown.Cell(stock.Name)}) — {basis}:"
        );
        result.AppendLine();
        result.AppendLine("| Period End | FY | Period | Value | Unit | Form | Filed | Accession |");
        result.AppendLine("|-----------|---:|--------|------:|------|------|-------|-----------|");
        foreach (var f in perPeriod)
        {
            result.AppendLine(
                $"| {f.PeriodEnd:yyyy-MM-dd} | {f.FiscalYear} | "
                    + $"{f.FiscalPeriod.NameForHumans()} | "
                    + $"{FactMarkdown.Value(f.Value, f.Unit)} | "
                    + $"{FactMarkdown.Cell(f.Unit)} | "
                    + $"{FactMarkdown.Cell(f.Form?.DisplayName)} | "
                    + $"{f.FiledDate:yyyy-MM-dd} | "
                    + $"{FactMarkdown.Cell(f.AccessionNumber)} |"
            );
        }

        return result.ToString();
    }

    private static string RenderComparisonTable(
        string concept,
        int fiscalYear,
        SecFiscalPeriod period,
        List<(string Ticker, string Name, FinancialFact Fact)> rows,
        List<string> skipped
    )
    {
        var result = new StringBuilder();
        result.AppendLine($"{concept} — {fiscalYear} {period.NameForHumans()} peer comparison:");
        result.AppendLine();
        result.AppendLine("| Ticker | Company | Value | Unit | Form | Filed |");
        result.AppendLine("|--------|---------|------:|------|------|-------|");
        foreach (var (ticker, name, fact) in rows)
        {
            result.AppendLine(
                $"| {FactMarkdown.Cell(ticker)} | {FactMarkdown.Cell(name)} | "
                    + $"{FactMarkdown.Value(fact.Value, fact.Unit)} | "
                    + $"{FactMarkdown.Cell(fact.Unit)} | "
                    + $"{FactMarkdown.Cell(fact.Form?.DisplayName)} | "
                    + $"{fact.FiledDate:yyyy-MM-dd} |"
            );
        }

        if (rows.Count == 0)
            result.AppendLine(
                $"\n_No company reported '{concept}' for {fiscalYear} "
                    + $"{period.NameForHumans()}._"
            );

        if (skipped.Count > 0)
            result.AppendLine($"\n_Skipped: {string.Join(", ", skipped)}._");

        return result.ToString();
    }

    // AccessionNumber is the stable final tiebreak for same-day amendments
    // (Postgres has no implicit row order). `asOriginallyReported` flips the
    // filing-date / accession ordering to ascending so the earliest filing wins.
    private static FinancialFact PickBestFact(
        IEnumerable<FinancialFact> facts,
        IReadOnlyDictionary<Guid, int> conceptPriority,
        bool asOriginallyReported = false
    )
    {
        var byPriority = facts.OrderBy(f => conceptPriority[f.FinancialConceptId]);
        return asOriginallyReported
            ? byPriority.ThenBy(f => f.FiledDate).ThenBy(f => f.AccessionNumber).First()
            : byPriority
                .ThenByDescending(f => f.FiledDate)
                .ThenByDescending(f => f.AccessionNumber)
                .First();
    }

    private async Task<Dictionary<Guid, int>> ResolveConceptPriority(
        IReadOnlyList<FinancialConceptAliases.ConceptRef> conceptRefs
    )
    {
        var taxonomies = conceptRefs.Select(c => c.Taxonomy).Distinct().ToList();
        var tags = conceptRefs.Select(c => c.Tag).Distinct().ToList();
        // (taxonomy, tag) → position in the alias's ordered tag list; lower
        // index is the alias's preferred/primary concept.
        var priorityByPair = new Dictionary<(FactTaxonomy, string), int>();
        for (var i = 0; i < conceptRefs.Count; i++)
            priorityByPair.TryAdd((conceptRefs[i].Taxonomy, conceptRefs[i].Tag), i);

        var rows = await _financialConceptRepository
            .GetMatching(taxonomies, tags)
            .Select(c => new
            {
                c.Id,
                c.Taxonomy,
                c.Tag,
            })
            .ToListAsync();

        // GetMatching is a taxonomy×tag cross product over a tiny alias tag set
        // (over-fetch is negligible here); narrow to the alias's exact pairs.
        return rows.Where(r => priorityByPair.ContainsKey((r.Taxonomy, r.Tag)))
            .ToDictionary(r => r.Id, r => priorityByPair[(r.Taxonomy, r.Tag)]);
    }

    // Accepts an absent bound (null/blank → no bound) but rejects a non-empty
    // value that is not an ISO yyyy-MM-dd date, so a typo can't silently widen
    // the result set.
    private static bool TryParseBound(string value, out DateOnly? bound)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            bound = null;
            return true;
        }

        if (
            DateOnly.TryParseExact(
                value.Trim(),
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsed
            )
        )
        {
            bound = parsed;
            return true;
        }

        bound = null;
        return false;
    }

    private static string SupportedAliasesNote()
    {
        return "Supported: "
            + $"{string.Join(", ", FinancialConceptAliases.SupportedAliases)} "
            + "(common synonyms like 'sales', 'r&d', 'ocf' also work).";
    }

    private Task<string> Execute(Func<Task<string>> work, string toolName, string context) =>
        McpToolExecutor.Execute(work, _logger, toolName, context, ReportError);

    private Task ReportError(string toolName, string message, string stackTrace, string context)
    {
        return _errorManager.Create(ErrorSource.McpTool, toolName, message, stackTrace, context);
    }
}
