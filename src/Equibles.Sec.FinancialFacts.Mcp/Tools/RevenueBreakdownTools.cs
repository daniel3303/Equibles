using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Equibles.CommonStocks.Repositories;
using Equibles.CommonStocks.Repositories.Extensions;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.BusinessLogic.Extensions;
using Equibles.Mcp;
using Equibles.Mcp.Helpers;
using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Data.Statements;
using Equibles.Sec.FinancialFacts.Mcp.Helpers;
using Equibles.Sec.FinancialFacts.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace Equibles.Sec.FinancialFacts.Mcp.Tools;

[McpServerToolType]
public class RevenueBreakdownTools
{
    // Issuers moved geography/product from us-gaap to the srt taxonomy in 2018; both
    // QNames identify the same axis, so each bucket accepts both spellings.
    private static readonly string[] SegmentAxes = ["us-gaap:StatementBusinessSegmentsAxis"];
    private static readonly string[] GeographyAxes =
    [
        "srt:StatementGeographicalAxis",
        "us-gaap:StatementGeographicalAxis",
    ];
    private static readonly string[] ProductAxes =
    [
        "srt:ProductOrServiceAxis",
        "us-gaap:ProductOrServiceAxis",
    ];
    private static readonly string[] AllAxes = [.. SegmentAxes, .. GeographyAxes, .. ProductAxes];

    // The shortest span a fiscal year can cover — 52 weeks on a 52/53-week calendar with
    // headroom for short transition years (mirrors FinancialStatementsHelper).
    private const int MinAnnualSpanDays = 350;

    private const int DefaultYears = 8;
    private const int MaxYearsCap = 12;

    private readonly FinancialFactRepository _financialFactRepository;
    private readonly FinancialConceptRepository _financialConceptRepository;
    private readonly CommonStockRepository _commonStockRepository;
    private readonly McpToolRunner _runner;

    public RevenueBreakdownTools(
        FinancialFactRepository financialFactRepository,
        FinancialConceptRepository financialConceptRepository,
        CommonStockRepository commonStockRepository,
        ErrorManager errorManager,
        ILogger<RevenueBreakdownTools> logger
    )
    {
        _financialFactRepository = financialFactRepository;
        _financialConceptRepository = financialConceptRepository;
        _commonStockRepository = commonStockRepository;
        _runner = new McpToolRunner(logger, errorManager.AsMcpErrorReporter());
    }

    [McpServerTool(Name = "GetRevenueBreakdown")]
    [Description(
        "Get a company's revenue disaggregated by business segment, geography and "
            + "product/service, from the dimensional XBRL facts the issuer tags in its own "
            + "filings. Annual fiscal years only, latest restated values, one table per axis "
            + "the company reports; values are as-reported and never estimated."
    )]
    public Task<string> GetRevenueBreakdown(
        [Description("Stock ticker symbol (e.g., AAPL, MSFT)")] string ticker,
        [Description("Most recent fiscal years to include (default 8, max 12)")]
            int maxYears = DefaultYears
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

                if (!FinancialConceptAliases.TryResolve("revenue", out var conceptRefs))
                    return "No revenue concepts are configured.";

                var taxonomies = conceptRefs.Select(r => r.Taxonomy).Distinct().ToList();
                var tags = conceptRefs.Select(r => r.Tag).ToList();
                var conceptIds = await _financialConceptRepository
                    .GetMatching(taxonomies, tags)
                    .Select(c => c.Id)
                    .ToListAsync();
                if (conceptIds.Count == 0)
                    return $"No revenue data has been ingested for {stock.Ticker}.";

                // Annual revenue facts carrying exactly one dimension on a known axis.
                // Cross-cut facts (e.g. segment × geography) are excluded — including
                // them in a single-axis mix would double-count the revenue they slice.
                var rows = await _financialFactRepository
                    .GetByStock(stock)
                    .Where(f =>
                        conceptIds.Contains(f.FinancialConceptId)
                        && f.PeriodType == FactPeriodType.Duration
                        && f.PeriodStart.AddDays(MinAnnualSpanDays) <= f.PeriodEnd
                        && f.DimensionsKey != ""
                        && f.Dimensions.Count == 1
                        && AllAxes.Contains(f.Dimensions.First().Axis)
                    )
                    .Select(f => new DimensionalRevenueRow(
                        f.Dimensions.First().Axis,
                        f.Dimensions.First().Member,
                        f.PeriodEnd,
                        f.Value,
                        f.Unit,
                        f.FiledDate
                    ))
                    .ToListAsync();

                if (rows.Count == 0)
                    return $"{stock.Ticker} has no dimensional revenue tagging on record — "
                        + "the issuer reports revenue as a consolidated total only.";

                var years = Math.Clamp(maxYears, 1, MaxYearsCap);
                var result = new StringBuilder();
                result.AppendLine(
                    $"Revenue breakdown for {stock.Ticker} ({FactMarkdown.Cell(stock.Name)}) — "
                        + "annual fiscal years, latest restated values:"
                );
                AppendAxis(result, "By segment", rows, SegmentAxes, years);
                AppendAxis(result, "By geography", rows, GeographyAxes, years);
                AppendAxis(result, "By product & service", rows, ProductAxes, years);
                return result.ToString();
            },
            "GetRevenueBreakdown",
            $"ticker: {FactMarkdown.Clean(ticker)}"
        );
    }

    private static void AppendAxis(
        StringBuilder result,
        string title,
        List<DimensionalRevenueRow> rows,
        string[] axes,
        int maxYears
    )
    {
        var (unit, periodEnds, members) = BuildAxisSeries(rows, axes, maxYears);
        if (members.Count == 0)
            return;

        result.AppendLine();
        result.AppendLine($"**{title}** ({FactMarkdown.Cell(unit)}):");
        result.AppendLine();
        result.AppendLine(
            "| Component | " + string.Join(" | ", periodEnds.Select(p => $"{p:yyyy-MM-dd}")) + " |"
        );
        result.AppendLine("|-----------|" + string.Concat(periodEnds.Select(_ => "---:|")));
        foreach (var member in members)
        {
            var cells = member.Values.Select(v =>
                v.HasValue ? FactMarkdown.Value(v.Value, unit) : "—"
            );
            result.AppendLine(
                $"| {FactMarkdown.Cell(member.Label)} | " + string.Join(" | ", cells) + " |"
            );
        }
    }

    // Pivot one axis's rows into period-end columns (oldest first) × member rows. Filings
    // re-report comparative prior years, so the latest-filed fact wins per (member,
    // period-end); the axis is pinned to the latest-filed fact's unit so a reporting-
    // currency change can't mix currencies in one series.
    internal static (
        string Unit,
        List<DateOnly> PeriodEnds,
        List<(string Label, List<decimal?> Values)> Members
    ) BuildAxisSeries(List<DimensionalRevenueRow> rows, string[] axes, int maxYears)
    {
        var current = rows.Where(r => axes.Contains(r.Axis))
            .GroupBy(r => (r.Member, r.PeriodEnd))
            .Select(g => g.OrderByDescending(r => r.FiledDate).First())
            .ToList();
        if (current.Count == 0)
            return (null, [], []);

        var unit = current.OrderByDescending(r => r.FiledDate).First().Unit;
        current = current.Where(r => r.Unit == unit).ToList();

        var periodEnds = current
            .Select(r => r.PeriodEnd)
            .Distinct()
            .OrderByDescending(d => d)
            .Take(maxYears)
            .OrderBy(d => d)
            .ToList();

        var latest = periodEnds[^1];
        var members = current
            .GroupBy(r => r.Member)
            .Select(g => new { g.Key, ByPeriod = g.ToDictionary(r => r.PeriodEnd, r => r.Value) })
            .Select(m => new
            {
                m.Key,
                Latest = m.ByPeriod.TryGetValue(latest, out var v) ? v : (decimal?)null,
                Values = periodEnds
                    .Select(p => m.ByPeriod.TryGetValue(p, out var pv) ? pv : (decimal?)null)
                    .ToList(),
            })
            .Where(m => m.Values.Any(v => v.HasValue))
            .OrderByDescending(m => m.Latest ?? decimal.MinValue)
            .ThenBy(m => m.Key)
            .Select(m => (Humanize(m.Key), m.Values))
            .ToList();
        return (unit, periodEnds, members);
    }

    // Display label from the XBRL member QName: ISO country members get their English
    // name; everything else drops the Member suffix and spaces the PascalCase local name.
    internal static string Humanize(string memberQName)
    {
        var colon = memberQName.IndexOf(':');
        var prefix = colon > 0 ? memberQName[..colon] : "";
        var local = colon > 0 ? memberQName[(colon + 1)..] : memberQName;

        if (prefix == "country")
        {
            try
            {
                return new RegionInfo(local).EnglishName;
            }
            catch (ArgumentException)
            {
                return local;
            }
        }

        if (local.EndsWith("Member", StringComparison.Ordinal) && local.Length > "Member".Length)
            local = local[..^"Member".Length];
        return Regex.Replace(local, "(?<=[a-z0-9])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])", " ");
    }

    internal sealed record DimensionalRevenueRow(
        string Axis,
        string Member,
        DateOnly PeriodEnd,
        decimal Value,
        string Unit,
        DateOnly FiledDate
    );
}
