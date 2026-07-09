using System.ComponentModel;
using System.Globalization;
using System.Text;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.CommonStocks.Repositories.Extensions;
using Equibles.Core.Extensions;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.BusinessLogic.Extensions;
using Equibles.Errors.Data.Models;
using Equibles.Mcp;
using Equibles.Mcp.Helpers;
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
    private readonly McpToolRunner _runner;

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
        _runner = new McpToolRunner(logger, errorManager.AsMcpErrorReporter());
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
        return _runner.Execute(
            async () =>
            {
                if (string.IsNullOrWhiteSpace(ticker))
                    return "A ticker symbol is required.";

                if (string.IsNullOrWhiteSpace(concept))
                    return $"A concept is required. {SupportedAliasesNote()}";

                var (stock, stockError) = await _commonStockRepository.ResolveByTicker(ticker);
                if (stockError != null)
                    return stockError;

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
                    .GetConsolidatedByStock(stock)
                    .Where(f => conceptPriority.Keys.Contains(f.FinancialConceptId))
                    .ToListAsync();

                // Reported discrete fourth quarters hide under fp=FY — the same
                // fiscal identity as the annual figure — so grouping by stamp
                // alone never surfaces them as Q4 rows. Promote them first (see
                // ReportedQuarterPromotion; a full-year total can never qualify).
                var allFacts = ReportedQuarterPromotion.WithPromotedFourthQuarters(facts);

                // Form / period-end filtering in memory: a single concept's
                // history is small, and DocumentType is a value-converted type
                // so equality is kept off the SQL side for provider safety.
                var filtered = allFacts.Where(f =>
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
        return _runner.Execute(
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
                // A Q4 request must also load the year's fp=FY rows: filers whose
                // Company Facts carry Q4 frames report the discrete fourth quarter
                // under the FullYear stamp (see ReportedQuarterPromotion).
                var includeFullYearForQ4 = period == SecFiscalPeriod.Q4;
                var facts = await _financialFactRepository
                    .GetConsolidatedByStocks(stockIds)
                    .Where(f =>
                        f.FiscalYear == fiscalYear
                        && (
                            f.FiscalPeriod == period
                            || (includeFullYearForQ4 && f.FiscalPeriod == SecFiscalPeriod.FullYear)
                        )
                        && conceptPriority.Keys.Contains(f.FinancialConceptId)
                    )
                    .ToListAsync();

                if (includeFullYearForQ4)
                {
                    // Promote per company: only a quarter-span row ending exactly
                    // where the company's own year does is that year's fourth
                    // quarter — comparative re-filings in the slice end earlier
                    // and belong to the prior year. The remaining fp=FY rows
                    // (the annual totals) are dropped, never compared as Q4.
                    facts = facts
                        .Where(f => f.FiscalPeriod == SecFiscalPeriod.Q4)
                        .Concat(
                            facts
                                .GroupBy(f => f.CommonStockId)
                                .SelectMany(
                                    ReportedQuarterPromotion.PromotedFourthQuartersForYearSlice
                                )
                        )
                        .ToList();
                }

                // Same deterministic pick as GetFinancialFact: the alias's
                // primary tag wins, then the latest filing, then accession as a
                // stable tiebreak for same-day amendments (Postgres has no
                // implicit row order).
                var bestByStock = facts
                    .GroupBy(f => f.CommonStockId)
                    .ToDictionary(g => g.Key, g => PickBestFact(g, conceptPriority));

                var (rows, skipped) = BuildComparisonRows(requested, stockByTicker, bestByStock);

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
        var result = MarkdownTable.Start(
            $"{concept} for {stock.Ticker} ({FactMarkdown.Cell(stock.Name)}) — {basis}:",
            "| Period End | FY | Period | Value | Unit | Form | Filed | Accession |",
            "|-----------|---:|--------|------:|------|------|-------|-----------|"
        );
        result.AppendRows(
            perPeriod,
            f =>
                $"| {f.PeriodEnd:yyyy-MM-dd} | {f.FiscalYear} | "
                + $"{f.FiscalPeriod.NameForHumans()} | "
                + $"{FactMarkdown.Value(f.Value, f.Unit)} | "
                + $"{FactMarkdown.Cell(f.Unit)} | "
                + $"{FactMarkdown.Cell(f.Form?.DisplayName)} | "
                + $"{f.FiledDate:yyyy-MM-dd} | "
                + $"{FactMarkdown.Cell(f.AccessionNumber)} |"
        );

        return result.ToString();
    }

    private static (
        List<(string Ticker, string Name, FinancialFact Fact)> Rows,
        List<string> Skipped
    ) BuildComparisonRows(
        IReadOnlyList<string> requested,
        IReadOnlyDictionary<string, CommonStock> stockByTicker,
        IReadOnlyDictionary<Guid, FinancialFact> bestByStock
    )
    {
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
        return (rows, skipped);
    }

    private static string RenderComparisonTable(
        string concept,
        int fiscalYear,
        SecFiscalPeriod period,
        List<(string Ticker, string Name, FinancialFact Fact)> rows,
        List<string> skipped
    )
    {
        var result = MarkdownTable.Start(
            $"{concept} — {fiscalYear} {period.NameForHumans()} peer comparison:",
            "| Ticker | Company | Value | Unit | Form | Filed |",
            "|--------|---------|------:|------|------|-------|"
        );
        result.AppendRows(
            rows,
            r =>
                $"| {FactMarkdown.Cell(r.Ticker)} | {FactMarkdown.Cell(r.Name)} | "
                + $"{FactMarkdown.Value(r.Fact.Value, r.Fact.Unit)} | "
                + $"{FactMarkdown.Cell(r.Fact.Unit)} | "
                + $"{FactMarkdown.Cell(r.Fact.Form?.DisplayName)} | "
                + $"{r.Fact.FiledDate:yyyy-MM-dd} |"
        );

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
        var candidates = facts.ToList();

        // Prefer the fact whose duration matches the fiscal period's granularity, so a
        // quarter reads as the discrete three months and never the year-to-date total a
        // 10-Q tags under the same fiscal Q2/Q3 (the financials tab handles this in
        // FinancialStatementsHelper.PickCurrentlyReportedFact). Instants (balance sheet,
        // zero span) always qualify; if nothing matches the span, keep every candidate.
        var fiscalPeriod = candidates[0].FiscalPeriod;
        var preferredSpan = candidates
            .Where(f =>
            {
                if (f.PeriodType != FactPeriodType.Duration)
                    return true;
                var spanDays = f.PeriodEnd.DayNumber - f.PeriodStart.DayNumber;
                return fiscalPeriod == SecFiscalPeriod.FullYear
                    ? spanDays >= FiscalPeriodSpanDays.MinAnnualSpanDays
                    : spanDays <= FiscalPeriodSpanDays.MaxDiscreteQuarterDays;
            })
            .ToList();
        if (preferredSpan.Count > 0)
            candidates = preferredSpan;

        // Latest period end first: a filing re-reports comparative prior periods (the
        // prior-year quarter, prior fiscal years, the prior year-end balance instant)
        // under its own fiscal identity, and those tie the current figure on span and
        // filed date — ignoring the period end surfaces a comparative column (#1546).
        var byPeriod = candidates
            .OrderByDescending(f => f.PeriodEnd)
            .ThenBy(f => conceptPriority[f.FinancialConceptId]);
        return asOriginallyReported
            ? byPeriod.ThenBy(f => f.FiledDate).ThenBy(f => f.AccessionNumber).First()
            : byPeriod
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
}
