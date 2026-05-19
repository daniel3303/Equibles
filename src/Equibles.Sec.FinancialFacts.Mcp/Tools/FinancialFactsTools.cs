using System.ComponentModel;
using System.Globalization;
using System.Text;
using Equibles.CommonStocks.Repositories;
using Equibles.Core.Extensions;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Equibles.Mcp;
using Equibles.Sec.Data.Models;
using Equibles.Sec.FinancialFacts.Data.Enums;
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
        return McpToolExecutor.Execute(
            async () =>
            {
                if (string.IsNullOrWhiteSpace(ticker))
                    return "A ticker symbol is required.";

                if (string.IsNullOrWhiteSpace(concept))
                    return "A concept is required. Supported: "
                        + $"{string.Join(", ", FinancialConceptAliases.SupportedAliases)} "
                        + "(common synonyms like 'sales', 'r&d', 'ocf' also work).";

                var stock = await _commonStockRepository.GetByTicker(
                    ticker.Trim().ToUpperInvariant()
                );
                if (stock == null)
                    return $"Stock '{ticker}' not found.";

                if (!FinancialConceptAliases.TryResolve(concept, out var conceptRefs))
                    return $"Unknown concept '{concept}'. Supported: "
                        + $"{string.Join(", ", FinancialConceptAliases.SupportedAliases)} "
                        + "(common synonyms like 'sales', 'r&d', 'ocf' also work).";

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
                    .Select(g =>
                    {
                        var byPriority = g.OrderBy(f => conceptPriority[f.FinancialConceptId]);
                        var ordered = asOriginallyReported
                            ? byPriority.ThenBy(f => f.FiledDate)
                            : byPriority.ThenByDescending(f => f.FiledDate);
                        return ordered.First();
                    })
                    .OrderByDescending(f => f.PeriodEnd)
                    .Take(Math.Clamp(maxResults, 1, MaxResultsCap))
                    .ToList();

                if (perPeriod.Count == 0)
                    return $"No '{concept}' data found for {stock.Ticker} with the given filters.";

                var basis = asOriginallyReported ? "as originally reported" : "latest restated";
                var result = new StringBuilder();
                result.AppendLine(
                    $"{concept} for {stock.Ticker} ({FactMarkdown.Cell(stock.Name)}) — {basis}:"
                );
                result.AppendLine();
                result.AppendLine(
                    "| Period End | FY | Period | Value | Unit | Form | Filed | Accession |"
                );
                result.AppendLine(
                    "|-----------|---:|--------|------:|------|------|-------|-----------|"
                );
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
            },
            _logger,
            "GetFinancialFact",
            $"ticker: {FactMarkdown.Clean(ticker)}, concept: {FactMarkdown.Clean(concept)}, "
                + $"form: {FactMarkdown.Clean(form)}",
            ReportError
        );
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

    private Task ReportError(string toolName, string message, string stackTrace, string context)
    {
        return _errorManager.Create(ErrorSource.McpTool, toolName, message, stackTrace, context);
    }
}
