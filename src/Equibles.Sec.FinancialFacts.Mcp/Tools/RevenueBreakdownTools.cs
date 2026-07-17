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
            + "the company reports; values are as-reported and never estimated. Rows within "
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

                if (!FinancialConceptAliases.TryResolve("revenue", out var conceptRefs))
                    return "No revenue concepts are configured.";

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
                result.AppendLine(
                    "_Components are shown exactly as the issuer tags them: a renamed member "
                        + "appears as a new row, so '—' gaps can reflect renames or "
                        + "reclassifications rather than zero revenue._"
                );
                AppendAxis(result, "By segment", rows, SegmentAxes, years, totals, displayTotals);
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
                return result.ToString();
            },
            "GetRevenueBreakdown",
            $"ticker: {FactMarkdown.Clean(ticker)}"
        );
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
        IReadOnlyDictionary<(DateOnly PeriodEnd, string Unit), decimal> displayTotals
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
            result.AppendLine(
                "| **Total revenue (consolidated)** | " + string.Join(" | ", totalCells) + " |"
            );

        // An issuer can tag a parent level alongside its components on the same
        // axis (AAPL's Product/Service next to iPhone/Mac/iPad; NVDA's Data
        // Center next to Compute/Networking). Those rows survive the disjoint-
        // scheme collapse because the schemes share or nest members, so the
        // column sums to well over consolidated revenue — say so rather than
        // let a consumer double-count.
        var overlaps = false;
        for (var i = 0; i < periodEnds.Count && !overlaps; i++)
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
    // different schemes together. Returns null when no full cover exists (not a clean overlap).
    //
    // Bounded and deterministic: geography axes carry well under 15 members, the candidate
    // subsets are enumerated by a bounded subset-sum walk anchored on the first uncovered
    // member, and ties are broken toward more subsets (more schemes) for stability.
    private static List<List<DimensionalRevenueRow>> FindFullTotalPartition(
        List<DimensionalRevenueRow> periodRows,
        decimal total,
        decimal tolerance
    )
    {
        // Order once so every subset and the final cover are produced deterministically.
        var members = periodRows
            .OrderByDescending(r => r.Value)
            .ThenBy(r => r.Member, StringComparer.Ordinal)
            .ToList();

        // All subsets summing to total within tolerance, each as a bitmask over `members`.
        var fullTotalSubsets = new List<(int Mask, decimal Deviation)>();
        EnumerateFullTotalSubsets(members, 0, 0, 0m, total, tolerance, fullTotalSubsets);
        if (fullTotalSubsets.Count == 0)
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
    private static void EnumerateFullTotalSubsets(
        List<DimensionalRevenueRow> members,
        int startIndex,
        int mask,
        decimal sum,
        decimal total,
        decimal tolerance,
        List<(int Mask, decimal Deviation)> output
    )
    {
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
                output
            );
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
        DateOnly FiledDate
    );
}
