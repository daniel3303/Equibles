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

    // srt:ConsolidationItemsAxis = us-gaap:OperatingSegmentsMember is a transparent
    // qualifier ("this figure is a pure operating-segment total"), not a second slice of
    // the value. A fact carrying it alongside one real axis still partitions total revenue,
    // so it must not be discarded as a cross-cut. Issuers such as Apple tag every operating
    // segment this way from FY2025 on, which otherwise drops the latest fiscal year (#3628).
    private const string ConsolidationItemsAxis = "srt:ConsolidationItemsAxis";
    private const string OperatingSegmentsMember = "us-gaap:OperatingSegmentsMember";

    // The shortest span a fiscal year can cover — 52 weeks on a 52/53-week calendar with
    // headroom for short transition years (mirrors FinancialStatementsHelper).
    private const int MinAnnualSpanDays = 350;

    // How close the latest filing's members must sum to consolidated total revenue to count
    // as a complete re-disaggregation (see ReconcileToTotal). Half a percent absorbs
    // rounding and minor unit scaling without admitting a partial amendment.
    private const decimal ReconciliationTolerance = 0.005m;

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
                // The OperatingSegments qualifier (see above) is the one allowed extra
                // dimension: it tags the fact as a pure segment total without slicing it.
                var rows = await _financialFactRepository
                    .GetByStock(stock)
                    .Where(f =>
                        conceptIds.Contains(f.FinancialConceptId)
                        && f.PeriodType == FactPeriodType.Duration
                        && f.PeriodStart.AddDays(MinAnnualSpanDays) <= f.PeriodEnd
                        && f.DimensionsKey != ""
                        && f.Dimensions.Count(d => AllAxes.Contains(d.Axis)) == 1
                        && f.Dimensions.All(d =>
                            AllAxes.Contains(d.Axis)
                            || (
                                d.Axis == ConsolidationItemsAxis
                                && d.Member == OperatingSegmentsMember
                            )
                        )
                    )
                    .Select(f => new DimensionalRevenueRow(
                        f.Dimensions.First(d => AllAxes.Contains(d.Axis)).Axis,
                        f.Dimensions.First(d => AllAxes.Contains(d.Axis)).Member,
                        f.PeriodEnd,
                        f.Value,
                        f.Unit,
                        f.FiledDate
                    ))
                    .ToListAsync();

                if (rows.Count == 0)
                    return $"{stock.Ticker} has no dimensional revenue tagging on record — "
                        + "the issuer reports revenue as a consolidated total only.";

                // Consolidated (no-dimension) total revenue — the figure each axis's members
                // must add up to, used to detect a complete re-disaggregation so a member a
                // later filing drops doesn't linger (see ReconcileToTotal). Several revenue
                // concepts can be tagged (e.g. Revenues plus the ASC 606 tag), so we keep one
                // candidate total per (period, unit, concept) — latest-filed wins — and the
                // members need only reconcile to any one of them.
                var consolidated = await _financialFactRepository
                    .GetConsolidatedByStock(stock)
                    .Where(f =>
                        conceptIds.Contains(f.FinancialConceptId)
                        && f.PeriodType == FactPeriodType.Duration
                        && f.PeriodStart.AddDays(MinAnnualSpanDays) <= f.PeriodEnd
                    )
                    .Select(f => new
                    {
                        f.PeriodEnd,
                        f.Unit,
                        f.FinancialConceptId,
                        f.Value,
                        f.FiledDate,
                    })
                    .ToListAsync();
                var totals = consolidated
                    .GroupBy(f => (f.PeriodEnd, f.Unit))
                    .ToDictionary(
                        g => g.Key,
                        g =>
                            (IReadOnlyList<decimal>)
                                g.GroupBy(f => f.FinancialConceptId)
                                    .Select(c =>
                                        c.OrderByDescending(f => f.FiledDate).First().Value
                                    )
                                    .ToList()
                    );

                var years = Math.Clamp(maxYears, 1, MaxYearsCap);
                var result = new StringBuilder();
                result.AppendLine(
                    $"Revenue breakdown for {stock.Ticker} ({FactMarkdown.Cell(stock.Name)}) — "
                        + "annual fiscal years, latest restated values:"
                );
                AppendAxis(result, "By segment", rows, SegmentAxes, years, totals);
                AppendAxis(result, "By geography", rows, GeographyAxes, years, totals);
                AppendAxis(result, "By product & service", rows, ProductAxes, years, totals);
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
        int maxYears,
        IReadOnlyDictionary<(DateOnly PeriodEnd, string Unit), IReadOnlyList<decimal>> totals
    )
    {
        var (unit, periodEnds, members) = BuildAxisSeries(rows, axes, maxYears, totals);
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

    // Resolve the surviving (member, period) facts for one axis, one row per cell — all in a
    // single pinned unit. The default rule keeps the latest-filed fact per (member, period) —
    // correct for a restatement that re-reports a member with a new value. It is wrong when a
    // later filing instead *drops* a member it has reclassified away (NVDA's Singapore, AMD's
    // Japan/Europe, KO/CAT renames): nothing supersedes the dropped member, so it lingers from
    // the older filing and the axis double-counts it.
    //
    // The fix is arithmetic, not pattern-matching: for each period, if the latest filing's own
    // members already reconcile to a consolidated total revenue (in the same unit), that filing
    // is a complete re-disaggregation — use only its members and discard anything carried over
    // from older filings. Otherwise the latest filing only restated some members (a partial
    // amendment), so fall back to the latest-filed-per-member merge that carries un-amended
    // members forward.
    private static List<DimensionalRevenueRow> ReconcileToTotal(
        IEnumerable<DimensionalRevenueRow> axisRowsInUnit,
        string unit,
        IReadOnlyDictionary<(DateOnly PeriodEnd, string Unit), IReadOnlyList<decimal>> totals
    )
    {
        var result = new List<DimensionalRevenueRow>();
        foreach (var period in axisRowsInUnit.GroupBy(r => r.PeriodEnd))
        {
            var latestFiled = period.Max(r => r.FiledDate);
            var latestFiling = period.Where(r => r.FiledDate == latestFiled).ToList();
            var latestSum = latestFiling.Sum(r => r.Value);

            // Compared against the consolidated total in the SAME unit as the pinned members.
            // Several revenue concepts may each report a total; the members complete the period
            // if they reconcile to any one of them (e.g. ASC 606 revenue vs. total Revenues).
            var complete =
                totals.TryGetValue((period.Key, unit), out var candidates)
                && candidates.Any(total =>
                    total != 0m
                    && Math.Abs(latestSum - total) <= Math.Abs(total) * ReconciliationTolerance
                );

            if (complete)
            {
                // Latest filing re-disaggregates the whole period — keep only its members,
                // collapsing any duplicate member within the same filing to its first fact.
                result.AddRange(latestFiling.GroupBy(r => r.Member).Select(g => g.First()));
            }
            else
            {
                // Partial amendment — latest-filed fact wins per member, older members carried.
                result.AddRange(
                    period
                        .GroupBy(r => r.Member)
                        .Select(g => g.OrderByDescending(r => r.FiledDate).First())
                );
            }
        }
        return result;
    }

    // Pivot one axis's rows into period-end columns (oldest first) × member rows. Filings
    // re-report comparative prior years, so the latest-filed fact wins per (member,
    // period-end); the axis is pinned to the latest-filed fact's unit so a reporting-
    // currency change can't mix currencies in one series. ReconcileToTotal then runs on the
    // single-unit rows — when a later filing completely re-disaggregates a period, members it
    // dropped must not linger from an older filing.
    internal static (
        string Unit,
        List<DateOnly> PeriodEnds,
        List<(string Label, List<decimal?> Values)> Members
    ) BuildAxisSeries(
        List<DimensionalRevenueRow> rows,
        string[] axes,
        int maxYears,
        IReadOnlyDictionary<(DateOnly PeriodEnd, string Unit), IReadOnlyList<decimal>> totals
    )
    {
        var axisRows = rows.Where(r => axes.Contains(r.Axis)).ToList();
        if (axisRows.Count == 0)
            return (null, [], []);

        // Pin the unit first (latest-filed fact's unit) so the reconciliation sum and the
        // consolidated total are always in the same currency.
        var unit = axisRows.OrderByDescending(r => r.FiledDate).First().Unit;
        var current = ReconcileToTotal(axisRows.Where(r => r.Unit == unit), unit, totals);
        if (current.Count == 0)
            return (null, [], []);

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
