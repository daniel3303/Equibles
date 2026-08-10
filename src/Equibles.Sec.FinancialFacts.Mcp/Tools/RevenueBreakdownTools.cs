using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Equibles.CommonStocks.Data.Models;
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

    [McpServerTool(
        Name = "GetRevenueBreakdown",
        Title = "Revenue Breakdown by Segment",
        ReadOnly = true
    )]
    [Description(
        "Get a company's revenue disaggregated by business segment, geography and "
            + "product/service — plus operating income by segment when the issuer tags it, so "
            + "segment profitability and margins are answerable — from the dimensional XBRL "
            + "facts the issuer tags in its own "
            + "filings. Annual fiscal years only, latest restated values, one table per axis "
            + "the company reports; source values are as-reported and never estimated, while "
            + "segment operating margin is derived as operating income divided by revenue for "
            + "the same folded raw member QName and exact period. Rows within "
            + "one table can OVERLAP when the issuer tags several granularities on the same "
            + "axis (a parent segment alongside its components), so never sum rows to derive "
            + "total revenue — use the consolidated total row each table carries. For "
            + "consolidated figures use GetFinancialStatement or GetFinancialFact."
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

                // Load the revenue and segment-income families independently. A fresh or
                // restricted corpus can contain valid OperatingIncomeLoss segment facts before
                // any revenue-alias concept row exists; that must not suppress the income table.
                FinancialConceptAliases.TryResolve("revenue", out var conceptRefs);

                var taxonomies = conceptRefs.Select(r => r.Taxonomy).Distinct().ToList();
                var tags = conceptRefs.Select(r => r.Tag).ToList();
                // conceptId → position of its tag in the alias's ordered list;
                // the alias's primary tag wins when picking the consolidated
                // total to display (same rule as GetFinancialFact's pick).
                var priorityByPair = new Dictionary<(FactTaxonomy, string), int>();
                for (var i = 0; i < conceptRefs.Count; i++)
                    priorityByPair.TryAdd((conceptRefs[i].Taxonomy, conceptRefs[i].Tag), i);
                var conceptRows = await _financialConceptRepository
                    .GetMatching(taxonomies, tags)
                    .Select(c => new
                    {
                        c.Id,
                        c.Taxonomy,
                        c.Tag,
                    })
                    .ToListAsync();
                var priorityById = conceptRows
                    .Where(c => priorityByPair.ContainsKey((c.Taxonomy, c.Tag)))
                    .ToDictionary(c => c.Id, c => priorityByPair[(c.Taxonomy, c.Tag)]);
                var conceptIds = priorityById.Keys.ToList();

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
                        f.FiledDate,
                        f.PeriodStart
                    ))
                    .ToListAsync();

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

                // One display figure per (period, unit) for the tables' total
                // row: the alias's primary tag wins, then the latest filing —
                // mirroring GetFinancialFact's deterministic pick.
                var displayTotals = consolidated
                    .GroupBy(f => (f.PeriodEnd, f.Unit))
                    .ToDictionary(
                        g => g.Key,
                        g =>
                            g.OrderBy(f => priorityById[f.FinancialConceptId])
                                .ThenByDescending(f => f.FiledDate)
                                .First()
                                .Value
                    );

                var years = Math.Clamp(maxYears, 1, MaxYearsCap);
                var result = new StringBuilder();
                result.AppendLine(
                    $"Revenue breakdown for {stock.Ticker} ({FactMarkdown.Cell(stock.Name)}) — "
                        + "annual fiscal years, latest restated values:"
                );
                if (rows.Count == 0)
                {
                    result.AppendLine(
                        "_No dimensional revenue tagging is on record; segment profitability "
                            + "below is shown only when the issuer separately tags it._"
                    );
                }
                else
                {
                    result.AppendLine(
                        "_Components are shown exactly as the issuer tags them: a renamed member "
                            + "appears as a new row, so '—' gaps can reflect renames or "
                            + "reclassifications rather than zero revenue._"
                    );
                    AppendAxis(
                        result,
                        "By segment",
                        rows,
                        SegmentAxes,
                        years,
                        totals,
                        displayTotals
                    );
                    AppendAxis(
                        result,
                        "By geography",
                        rows,
                        GeographyAxes,
                        years,
                        totals,
                        displayTotals
                    );
                    AppendAxis(
                        result,
                        "By product & service",
                        rows,
                        ProductAxes,
                        years,
                        totals,
                        displayTotals
                    );
                }

                var hasSegmentOperatingIncome = await AppendSegmentOperatingIncome(
                    result,
                    stock,
                    years,
                    rows,
                    totals
                );
                if (rows.Count == 0 && !hasSegmentOperatingIncome)
                    return $"{stock.Ticker} has no dimensional revenue or segment operating "
                        + "income tagging on record.";

                return result.ToString();
            },
            "GetRevenueBreakdown",
            $"ticker: {FactMarkdown.Clean(ticker)}"
        );
    }

    // The profitability view of the segment cut: operating income tagged on the same
    // business-segment axis. Unallocated corporate costs mean segments rarely add up to
    // consolidated operating income, so this axis is rendered on its own reconciliation
    // (its own consolidated totals) and never mixed into the revenue tables above.
    private async Task<bool> AppendSegmentOperatingIncome(
        StringBuilder result,
        CommonStock stock,
        int years,
        List<DimensionalRevenueRow> revenueRows,
        IReadOnlyDictionary<(DateOnly PeriodEnd, string Unit), IReadOnlyList<decimal>> revenueTotals
    )
    {
        if (!FinancialConceptAliases.TryResolve("operating-income", out var conceptRefs))
            return false;

        var taxonomies = conceptRefs.Select(r => r.Taxonomy).Distinct().ToList();
        var tags = conceptRefs.Select(r => r.Tag).ToList();
        var conceptIds = await _financialConceptRepository
            .GetMatching(taxonomies, tags)
            .Select(c => c.Id)
            .ToListAsync();
        if (conceptIds.Count == 0)
            return false;

        var rows = await _financialFactRepository
            .GetByStock(stock)
            .Where(f =>
                conceptIds.Contains(f.FinancialConceptId)
                && f.PeriodType == FactPeriodType.Duration
                && f.PeriodStart.AddDays(MinAnnualSpanDays) <= f.PeriodEnd
                && f.DimensionsKey != ""
                && f.Dimensions.Count(d => SegmentAxes.Contains(d.Axis)) == 1
                && f.Dimensions.All(d =>
                    SegmentAxes.Contains(d.Axis)
                    || (d.Axis == ConsolidationItemsAxis && d.Member == OperatingSegmentsMember)
                )
            )
            .Select(f => new DimensionalRevenueRow(
                f.Dimensions.First(d => SegmentAxes.Contains(d.Axis)).Axis,
                f.Dimensions.First(d => SegmentAxes.Contains(d.Axis)).Member,
                f.PeriodEnd,
                f.Value,
                f.Unit,
                f.FiledDate,
                f.PeriodStart
            ))
            .ToListAsync();
        if (rows.Count == 0)
            return false;

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
                            .Select(c => c.OrderByDescending(f => f.FiledDate).First().Value)
                            .ToList()
            );
        var displayTotals = consolidated
            .GroupBy(f => (f.PeriodEnd, f.Unit))
            .ToDictionary(g => g.Key, g => g.OrderByDescending(f => f.FiledDate).First().Value);

        var incomeSeries = BuildAxisSeries(rows, SegmentAxes, years, totals);
        if (incomeSeries.Members.Count == 0)
            return false;

        AppendAxis(
            result,
            "Segment operating income",
            rows,
            SegmentAxes,
            years,
            totals,
            displayTotals,
            totalLabel: "Total operating income (consolidated)",
            checkOverlap: false
        );
        result.AppendLine(
            "_Segment operating income is tagged on the same business-segment axis as the revenue "
                + "facts; unallocated corporate costs mean the members need not add up to "
                + "consolidated operating income._"
        );

        var revenueSeries = BuildAxisSeries(revenueRows, SegmentAxes, years, revenueTotals);
        var marginSeries = BuildSegmentMarginSeries(revenueSeries, incomeSeries);
        if (marginSeries.Members.Count > 0)
        {
            AppendSeriesTable(result, "Segment operating margin", marginSeries);
            result.AppendLine(
                "_Derived as segment operating income ÷ revenue × 100 only where the same folded "
                    + "raw member QName and exact period match and revenue is positive; missing cells "
                    + "are not estimated._"
            );
        }

        return true;
    }

    // How far above the consolidated total a period's member sum must land before the
    // axis is flagged as carrying overlapping granularities. Looser than
    // ReconciliationTolerance so per-member rounding can never trip it; a genuine
    // parent-plus-components overlap overshoots by the whole parent.
    private const decimal OverlapNoteTolerance = 0.02m;

    private static void AppendAxis(
        StringBuilder result,
        string title,
        List<DimensionalRevenueRow> rows,
        string[] axes,
        int maxYears,
        IReadOnlyDictionary<(DateOnly PeriodEnd, string Unit), IReadOnlyList<decimal>> totals,
        IReadOnlyDictionary<(DateOnly PeriodEnd, string Unit), decimal> displayTotals,
        // The total row is labelled per axis: printing an operating-income total under
        // "Total revenue" would answer a revenue question with a profit figure.
        string totalLabel = "Total revenue (consolidated)",
        // The overlap warning below is a REVENUE rule — members that must add up to the
        // consolidated total. Segment operating income is not expected to add up (unallocated
        // corporate costs), so running it there fabricates a claim about the issuer's tagging.
        bool checkOverlap = true
    )
    {
        var series = BuildAxisSeries(rows, axes, maxYears, totals);
        if (series.Members.Count == 0)
            return;

        AppendSeriesTable(result, title, series);
        var (unit, periodEnds, members) = series;

        // The consolidated figure the members must be read against — lets a
        // consumer compute revenue shares and spot overlapping rows without a
        // second tool call. Costs nothing: the totals are already loaded.
        var totalCells = periodEnds
            .Select(p =>
                displayTotals.TryGetValue((p, unit), out var total)
                    ? FactMarkdown.Value(total, unit)
                    : "—"
            )
            .ToList();
        if (totalCells.Any(c => c != "—"))
            result.AppendLine($"| **{totalLabel}** | " + string.Join(" | ", totalCells) + " |");

        // An issuer can tag a parent level alongside its components on the same
        // axis (AAPL's Product/Service next to iPhone/Mac/iPad; NVDA's Data
        // Center next to Compute/Networking). Those rows survive the disjoint-
        // scheme collapse because the schemes share or nest members, so the
        // column sums to well over consolidated revenue — say so rather than
        // let a consumer double-count.
        var overlaps = false;
        for (var i = 0; i < periodEnds.Count && !overlaps && checkOverlap; i++)
        {
            if (!displayTotals.TryGetValue((periodEnds[i], unit), out var total) || total == 0m)
                continue;
            var memberSum = members.Sum(m => m.Values[i] ?? 0m);
            overlaps = memberSum - total > Math.Abs(total) * OverlapNoteTolerance;
        }
        if (overlaps)
            result.AppendLine(
                "\n_Rows on this axis overlap: the issuer tags more than one granularity "
                    + "(a parent component alongside its parts), so rows must NOT be summed — "
                    + "reconcile against the consolidated total row._"
            );

        // Older fiscal years beyond maxYears exist — say so instead of letting
        // the series read as the full history.
        var availableYears = rows.Where(r => axes.Contains(r.Axis) && r.Unit == unit)
            .Select(r => r.PeriodEnd)
            .Distinct()
            .Count();
        if (availableYears > periodEnds.Count)
            result.AppendLine(
                periodEnds.Count >= MaxYearsCap
                    ? $"\n_Showing the latest {periodEnds.Count} of {availableYears} fiscal "
                        + "years (the tool's maximum)._"
                    : $"\n_Showing the latest {periodEnds.Count} of {availableYears} fiscal "
                        + $"years — raise maxYears (max {MaxYearsCap}) to see more._"
            );
    }

    private static void AppendSeriesTable(StringBuilder result, string title, AxisSeries series)
    {
        result.AppendLine();
        result.AppendLine($"**{title}** ({FactMarkdown.Cell(series.Unit)}):");
        result.AppendLine();
        result.AppendLine(
            "| Component | "
                + string.Join(" | ", series.PeriodEnds.Select(p => $"{p:yyyy-MM-dd}"))
                + " |"
        );
        result.AppendLine("|-----------|" + string.Concat(series.PeriodEnds.Select(_ => "---:|")));
        foreach (var member in series.Members)
        {
            var cells = member.Values.Select(v =>
                v.HasValue ? FactMarkdown.Value(v.Value, series.Unit) : "—"
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
            var candidates = totals.TryGetValue((period.Key, unit), out var totalsForPeriod)
                ? totalsForPeriod
                : [];
            var complete = candidates.Any(total =>
                total != 0m
                && Math.Abs(latestSum - total) <= Math.Abs(total) * ReconciliationTolerance
            );

            List<DimensionalRevenueRow> periodRows;
            if (complete)
            {
                // Latest filing re-disaggregates the whole period — keep only its members,
                // collapsing any duplicate member within the same filing to its first fact.
                periodRows = latestFiling.GroupBy(r => r.Member).Select(g => g.First()).ToList();
            }
            else
            {
                // Partial amendment — latest-filed fact wins per member, older members carried.
                periodRows = period
                    .GroupBy(r => r.Member)
                    .Select(g => g.OrderByDescending(r => r.FiledDate).First())
                    .ToList();
            }

            // An issuer can tag two overlapping disaggregation schemes on the same axis in one
            // filing (e.g. a regional partition AND a by-country partition), each summing to
            // consolidated total revenue. Both survive the merge above, so the axis would show
            // ~2x actual revenue (#3897). Collapse to one scheme when the members partition
            // cleanly into 2+ full-total subsets; otherwise leave the period untouched.
            result.AddRange(CollapseOverlappingSchemes(periodRows, candidates));
        }
        return result;
    }

    // When the members of one period partition into 2+ DISJOINT subsets that EACH reconcile to
    // a consolidated total revenue (with no member left over), the issuer tagged multiple
    // overlapping schemes on the same axis. Keep only the MOST GRANULAR full-total subset (the
    // most members — the most informative view); every full-total subset is individually
    // correct, so this only chooses which correct view to show, never alters a number.
    //
    // Pure arithmetic, no member-name matching. The guard is strict: act only when the members
    // FULLY partition into >=2 disjoint full-total subsets. A single scheme, or a partial
    // overlap with no clean second full-total subset, is left unchanged — nothing is dropped.
    private static List<DimensionalRevenueRow> CollapseOverlappingSchemes(
        List<DimensionalRevenueRow> periodRows,
        IReadOnlyList<decimal> candidates
    )
    {
        if (periodRows.Count < 2)
        {
            return periodRows;
        }

        foreach (var total in candidates.Where(t => t != 0m).Distinct())
        {
            var tolerance = Math.Abs(total) * ReconciliationTolerance;

            // Skip the trivial case where the whole member set already equals the total — that
            // is one scheme, not an overlap of two.
            if (Math.Abs(periodRows.Sum(r => r.Value) - total) <= tolerance)
            {
                continue;
            }

            var partition = FindFullTotalPartition(periodRows, total, tolerance);
            if (partition != null && partition.Count >= 2)
            {
                // Keep the most granular subset; ties broken by the larger summed value, then a
                // stable member ordering, so the choice is deterministic.
                return partition
                    .OrderByDescending(subset => subset.Count)
                    .ThenByDescending(subset => subset.Sum(r => r.Value))
                    .ThenBy(subset => string.Join("|", subset.Select(r => r.Member).Order()))
                    .First();
            }
        }

        return periodRows;
    }

    // Partition the members into disjoint subsets that each sum to `total` (within tolerance),
    // covering every member exactly once. Returns the cover that minimises the total deviation
    // from `total` across its subsets — so the genuine schemes (each summing to the exact
    // consolidated figure) win over a tolerance-admitted near-miss that stitches members from
    // different schemes together. Returns null when no full cover exists (not a clean overlap)
    // or when any combinatorial bound trips — the period then passes through exactly as
    // reported, the search's existing fail-safe.
    //
    // The bounds are load-bearing, not defensive fluff. The walk is exhaustive (~2^n), and the
    // product axis breaks the "well under 15 members" assumption geography axes satisfy: a
    // pharma issuer tags one member per drug (PFE 76, JNJ 46, LLY 40 on
    // srt:ProductOrServiceAxis). Unbounded, one such call burned a CPU core indefinitely — and
    // past 32 members the `1 << i` bitmasks alias (the shift count wraps), which corrupted the
    // cover search into a literal non-terminating loop.
    private const int MaxCollapseMembers = 31;
    private const int MaxSubsetSearchNodes = 200_000;
    private const int MaxCoverSubsets = 4_096;

    private static List<List<DimensionalRevenueRow>> FindFullTotalPartition(
        List<DimensionalRevenueRow> periodRows,
        decimal total,
        decimal tolerance
    )
    {
        // Bitmask-width guard: 31 keeps every index inside the 32 bits the masks address. It
        // deliberately does not try to bound cost — mid-size axes are cheap to search and the
        // collapse is load-bearing for them — the node budget below is the cost bound.
        if (periodRows.Count > MaxCollapseMembers)
        {
            return null;
        }

        // Order once so every subset and the final cover are produced deterministically.
        var members = periodRows
            .OrderByDescending(r => r.Value)
            .ThenBy(r => r.Member, StringComparer.Ordinal)
            .ToList();

        // All subsets summing to total within tolerance, each as a bitmask over `members`.
        // The subset-count cap keeps the cover search's input no larger than it ever was under
        // the original assumption; a genuine overlap yields a handful of full-total subsets.
        var fullTotalSubsets = new List<(int Mask, decimal Deviation)>();
        var budget = MaxSubsetSearchNodes;
        EnumerateFullTotalSubsets(
            members,
            0,
            0,
            0m,
            total,
            tolerance,
            fullTotalSubsets,
            ref budget
        );
        if (fullTotalSubsets.Count == 0 || budget <= 0 || fullTotalSubsets.Count > MaxCoverSubsets)
        {
            return null;
        }

        var (bestMasks, found) = FindBestCover(members.Count, fullTotalSubsets, total);
        if (!found)
        {
            return null;
        }

        return bestMasks
            .Select(mask =>
                Enumerable
                    .Range(0, members.Count)
                    .Where(i => (mask & (1 << i)) != 0)
                    .Select(i => members[i])
                    .ToList()
            )
            .ToList();
    }

    // Depth-first enumeration of every subset of members[startIndex..] whose running sum reaches
    // `total` within tolerance, recorded as a bitmask plus its absolute deviation from total.
    //
    // `budget` counts down the visited nodes across the whole recursion: a set of near-equal
    // members can explore a large share of 2^n even under the member cap, so the budget stops
    // the walk in bounded time regardless of shape. An exhausted budget means the enumeration
    // is incomplete, so the caller must discard the result rather than act on a partial list.
    private static void EnumerateFullTotalSubsets(
        List<DimensionalRevenueRow> members,
        int startIndex,
        int mask,
        decimal sum,
        decimal total,
        decimal tolerance,
        List<(int Mask, decimal Deviation)> output,
        ref int budget
    )
    {
        if (budget <= 0)
        {
            return;
        }
        budget--;

        if (mask != 0 && Math.Abs(sum - total) <= tolerance)
        {
            output.Add((mask, Math.Abs(sum - total)));
            // A superset only adds positive values, moving further from total — so stop here.
            return;
        }
        if (sum - total > tolerance)
        {
            return;
        }

        for (var i = startIndex; i < members.Count; i++)
        {
            EnumerateFullTotalSubsets(
                members,
                i + 1,
                mask | (1 << i),
                sum + members[i].Value,
                total,
                tolerance,
                output,
                ref budget
            );
            if (budget <= 0)
            {
                return;
            }
        }
    }

    // Choose the disjoint cover of all members (each index used once) built from the full-total
    // subsets, minimising summed deviation, then preferring more subsets. Anchors each step on
    // the lowest uncovered index so the search is forced and bounded.
    private static (List<int> Masks, bool Found) FindBestCover(
        int memberCount,
        List<(int Mask, decimal Deviation)> subsets,
        decimal total
    )
    {
        var allCovered = (1 << memberCount) - 1;
        List<int> best = null;
        var bestDeviation = decimal.MaxValue;

        void Search(int covered, List<int> chosen, decimal deviation)
        {
            if (deviation >= bestDeviation)
            {
                return;
            }
            if (covered == allCovered)
            {
                if (
                    best == null
                    || deviation < bestDeviation
                    || (deviation == bestDeviation && chosen.Count > best.Count)
                )
                {
                    best = [.. chosen];
                    bestDeviation = deviation;
                }
                return;
            }

            var anchor = 0;
            while ((covered & (1 << anchor)) != 0)
            {
                anchor++;
            }

            foreach (var (mask, dev) in subsets)
            {
                if ((mask & (1 << anchor)) != 0 && (mask & covered) == 0)
                {
                    chosen.Add(mask);
                    Search(covered | mask, chosen, deviation + dev);
                    chosen.RemoveAt(chosen.Count - 1);
                }
            }
        }

        Search(0, [], 0m);
        return (best ?? [], best != null);
    }

    // Pivot one axis's rows into period-end columns (oldest first) × member rows. Each cell also
    // retains its exact duration start so a downstream ratio cannot join same-end/different-span
    // facts. Filings
    // re-report comparative prior years, so the latest-filed fact wins per (member,
    // period-end); the axis is pinned to the latest-filed fact's unit so a reporting-
    // currency change can't mix currencies in one series. ReconcileToTotal then runs on the
    // single-unit rows — when a later filing completely re-disaggregates a period, members it
    // dropped must not linger from an older filing.
    internal static AxisSeries BuildAxisSeries(
        List<DimensionalRevenueRow> rows,
        string[] axes,
        int maxYears,
        IReadOnlyDictionary<(DateOnly PeriodEnd, string Unit), IReadOnlyList<decimal>> totals
    )
    {
        var axisRows = rows.Where(r => axes.Contains(r.Axis)).ToList();
        if (axisRows.Count == 0)
            return new AxisSeries(null, [], []);

        // Pin the unit first (latest-filed fact's unit) so the reconciliation sum and the
        // consolidated total are always in the same currency.
        var unit = axisRows.OrderByDescending(r => r.FiledDate).First().Unit;
        var inUnit = axisRows.Where(r => r.Unit == unit);
        var current = ReconcileToTotal(inUnit, unit, totals);
        if (current.Count == 0)
            return new AxisSeries(null, [], []);

        var periodEnds = current
            .Select(r => r.PeriodEnd)
            .Distinct()
            .OrderByDescending(d => d)
            .Take(maxYears)
            .OrderBy(d => d)
            .ToList();

        var latest = periodEnds[^1];
        // Fold before pivoting, not only when revenue and income are joined. An issuer can respell
        // one member across filings (amd:DatacenterMember / amd:DataCenterMember); exact grouping
        // would produce two half-series and make one side of a later margin join disappear. The
        // latest-filed spelling fronts the merged series, and the latest-filed fact wins when two
        // spellings survive for the same period.
        var members = current
            .GroupBy(r => FoldMemberQName(r.Member), StringComparer.Ordinal)
            .Select(g => new
            {
                Key = g.OrderByDescending(r => r.FiledDate)
                    .ThenByDescending(r => r.PeriodEnd)
                    .First()
                    .Member,
                ByPeriod = g.GroupBy(r => r.PeriodEnd)
                    .ToDictionary(
                        pg => pg.Key,
                        pg => pg.OrderByDescending(r => r.FiledDate).First()
                    ),
            })
            .Select(m => new
            {
                m.Key,
                Latest = m.ByPeriod.TryGetValue(latest, out var latestRow)
                    ? latestRow.Value
                    : (decimal?)null,
                Values = periodEnds
                    .Select(p =>
                        m.ByPeriod.TryGetValue(p, out var row) ? row.Value : (decimal?)null
                    )
                    .ToList(),
                PeriodStarts = periodEnds
                    .Select(p =>
                        m.ByPeriod.TryGetValue(p, out var row) ? row.PeriodStart : (DateOnly?)null
                    )
                    .ToList(),
            })
            .Where(m => m.Values.Any(v => v.HasValue))
            .OrderByDescending(m => m.Latest ?? decimal.MinValue)
            .ThenBy(m => m.Key)
            .Select(m => new AxisMemberSeries(m.Key, Humanize(m.Key), m.Values, m.PeriodStarts))
            .ToList();
        return new AxisSeries(unit, periodEnds, members);
    }

    // REST parity for the derived margin axis: join the two independently selected axes by
    // issuer member QName, exact duration, and unit, never by display label or row position. The fold is
    // the corpus-backed XBRL identifier rule shared by the REST provider (case and underscores
    // drift across filings); it retains the namespace prefix, so equal-looking labels from two
    // different members cannot be combined.
    internal static AxisSeries BuildSegmentMarginSeries(AxisSeries revenue, AxisSeries income)
    {
        if (
            revenue.Members.Count == 0
            || income.Members.Count == 0
            || !string.Equals(revenue.Unit, income.Unit, StringComparison.Ordinal)
        )
            return new AxisSeries("%", [], []);

        var incomeByMember = income
            .Members.GroupBy(m => FoldMemberQName(m.Member), StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var incomePeriodIndex = income
            .PeriodEnds.Select((periodEnd, index) => (periodEnd, index))
            .ToDictionary(p => p.periodEnd, p => p.index);

        var members = new List<AxisMemberSeries>();
        var usedPeriods = new bool[revenue.PeriodEnds.Count];
        foreach (var revenueMember in revenue.Members)
        {
            if (
                !incomeByMember.TryGetValue(
                    FoldMemberQName(revenueMember.Member),
                    out var incomeMember
                )
            )
                continue;

            var values = new List<decimal?>();
            var any = false;
            for (var i = 0; i < revenue.PeriodEnds.Count; i++)
            {
                decimal? margin = null;
                if (
                    incomePeriodIndex.TryGetValue(revenue.PeriodEnds[i], out var incomeIndex)
                    && revenueMember.PeriodStartAt(i) == incomeMember.PeriodStartAt(incomeIndex)
                    && revenueMember.Values[i] is { } revenueValue
                    && revenueValue > 0m
                    && incomeMember.Values[incomeIndex] is { } incomeValue
                )
                {
                    margin = incomeValue / revenueValue * 100m;
                    any = true;
                    usedPeriods[i] = true;
                }
                values.Add(margin);
            }

            if (any)
            {
                members.Add(
                    new AxisMemberSeries(revenueMember.Member, revenueMember.Label, values)
                );
            }
        }

        if (members.Count == 0)
            return new AxisSeries("%", [], []);

        var keptIndexes = Enumerable
            .Range(0, revenue.PeriodEnds.Count)
            .Where(i => usedPeriods[i])
            .ToList();
        return new AxisSeries(
            "%",
            keptIndexes.Select(i => revenue.PeriodEnds[i]).ToList(),
            members
                .Select(m => new AxisMemberSeries(
                    m.Member,
                    m.Label,
                    keptIndexes.Select(i => m.Values[i]).ToList(),
                    keptIndexes.Select(i => m.PeriodStartAt(i)).ToList()
                ))
                .ToList()
        );
    }

    private static string FoldMemberQName(string member) =>
        member?.Replace("_", "").ToLowerInvariant();

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
        // The (?<!^[A-Z]) guard keeps a lone leading capital attached to the word
        // that follows: Apple's IPhoneMember reads "IPhone", never "I Phone".
        // Boundaries deeper in the name still split ("USSegment" → "US Segment").
        return Regex.Replace(
            local,
            "(?<=[a-z0-9])(?=[A-Z])|(?<=[A-Z])(?<!^[A-Z])(?=[A-Z][a-z])",
            " "
        );
    }

    internal sealed record DimensionalRevenueRow(
        string Axis,
        string Member,
        DateOnly PeriodEnd,
        decimal Value,
        string Unit,
        DateOnly FiledDate,
        DateOnly? PeriodStart = null
    );

    internal sealed record AxisMemberSeries(
        string Member,
        string Label,
        List<decimal?> Values,
        List<DateOnly?> PeriodStarts = null
    )
    {
        internal DateOnly? PeriodStartAt(int index) =>
            PeriodStarts != null && index < PeriodStarts.Count ? PeriodStarts[index] : null;
    }

    internal sealed record AxisSeries(
        string Unit,
        List<DateOnly> PeriodEnds,
        List<AxisMemberSeries> Members
    );
}
