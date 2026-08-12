using System.ComponentModel;
using System.Text;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.CommonStocks.Repositories.Extensions;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.BusinessLogic.Extensions;
using Equibles.Mcp;
using Equibles.Mcp.Helpers;
using Equibles.Sec.FinancialFacts.BusinessLogic.RevenueBreakdown;
using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Data.Statements;
using Equibles.Sec.FinancialFacts.Mcp.Helpers;
using Equibles.Sec.FinancialFacts.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace Equibles.Sec.FinancialFacts.Mcp.Tools;

// Selection rules (axes, reconciliation, cross-cut roll-up, member fold, rollup-member
// filter) live in RevenueBreakdownCore — the ONE lane shared with the commercial REST
// endpoint and portal, so the transports can never disagree on a company's member sets
// (EquiblesCommercial#7166). This class owns only the queries and the markdown rendering.
[McpServerToolType]
public class RevenueBreakdownTools
{
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

                var rows = await LoadSingleAxisRows(
                    stock,
                    conceptIds,
                    RevenueBreakdownCore.AllAxes
                );

                // Consolidated (no-dimension) total revenue — the figure each axis's members
                // must add up to, used to detect a complete re-disaggregation so a member a
                // later filing drops doesn't linger (see RevenueBreakdownCore.ReconcileToTotal).
                // Several revenue concepts can be tagged (e.g. Revenues plus the ASC 606 tag),
                // so we keep one candidate total per (period, unit, concept) — latest-filed
                // wins — and the members need only reconcile to any one of them.
                var consolidated = await _financialFactRepository
                    .GetConsolidatedByStock(stock)
                    .Where(f =>
                        conceptIds.Contains(f.FinancialConceptId)
                        && f.PeriodType == FactPeriodType.Duration
                        && f.PeriodStart.AddDays(FiscalPeriodSpanDays.MinAnnualSpanDays)
                            <= f.PeriodEnd
                        && f.PeriodStart.AddDays(FiscalPeriodSpanDays.MaxAnnualSpanDays)
                            >= f.PeriodEnd
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

                // Filers can move a cut to TWO-dimensional tagging entirely (SoFi tags every
                // product fact product × segment from FY2023; XOM tags segments and
                // geographies only in cross-cuts after its 2022 reorganisation) — the
                // single-axis query rightly excludes those as cross-cuts, which froze the
                // axis at the last single-axis fiscal year. Roll the cross-cuts up per axis
                // family, only for periods with no single-axis cut; the core picks ONE
                // partner family per period against the consolidated totals so the same
                // revenue is never counted once per partner axis.
                var crossCuts = await LoadCrossCutRows(stock, conceptIds);
                foreach (var family in RevenueBreakdownCore.AxisFamilies)
                {
                    rows.AddRange(
                        RevenueBreakdownCore.RollUpCrossCuts(crossCuts, family, rows, totals)
                    );
                }

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
                        RevenueBreakdownCore.SegmentAxes,
                        years,
                        totals,
                        displayTotals
                    );
                    AppendAxis(
                        result,
                        "By geography",
                        rows,
                        RevenueBreakdownCore.GeographyAxes,
                        years,
                        totals,
                        displayTotals
                    );
                    AppendAxis(
                        result,
                        "By product & service",
                        rows,
                        RevenueBreakdownCore.ProductAxes,
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

    // Annual facts carrying exactly one dimension on a requested axis. Cross-cut facts
    // (e.g. segment × geography) are excluded — including them in a single-axis mix
    // would double-count the revenue they slice. The OperatingSegments qualifier is the
    // one allowed extra dimension: it tags the fact as a pure segment total without
    // slicing it.
    private async Task<List<DimensionalRevenueRow>> LoadSingleAxisRows(
        CommonStock stock,
        List<Guid> conceptIds,
        string[] axes
    )
    {
        return await _financialFactRepository
            .GetByStock(stock)
            .Where(f =>
                conceptIds.Contains(f.FinancialConceptId)
                && f.PeriodType == FactPeriodType.Duration
                && f.PeriodStart.AddDays(FiscalPeriodSpanDays.MinAnnualSpanDays) <= f.PeriodEnd
                && f.PeriodStart.AddDays(FiscalPeriodSpanDays.MaxAnnualSpanDays) >= f.PeriodEnd
                && f.DimensionsKey != ""
                && f.Dimensions.Count(d => axes.Contains(d.Axis)) == 1
                && f.Dimensions.All(d =>
                    axes.Contains(d.Axis)
                    || (
                        d.Axis == RevenueBreakdownCore.ConsolidationItemsAxis
                        && d.Member == RevenueBreakdownCore.OperatingSegmentsMember
                    )
                )
            )
            .Select(f => new DimensionalRevenueRow(
                f.Dimensions.First(d => axes.Contains(d.Axis)).Axis,
                f.Dimensions.First(d => axes.Contains(d.Axis)).Member,
                f.PeriodEnd,
                f.Value,
                f.Unit,
                f.FiledDate,
                f.PeriodStart,
                f.FiscalYear
            ))
            .ToListAsync();
    }

    // Annual facts carrying exactly TWO dimensions on known breakdown axes (a cross-cut
    // like product × segment), qualifier tolerated like the single-axis query.
    private async Task<List<CrossCutRevenueRow>> LoadCrossCutRows(
        CommonStock stock,
        List<Guid> conceptIds
    )
    {
        var facts = await _financialFactRepository
            .GetByStock(stock)
            .Where(f =>
                conceptIds.Contains(f.FinancialConceptId)
                && f.PeriodType == FactPeriodType.Duration
                && f.PeriodStart.AddDays(FiscalPeriodSpanDays.MinAnnualSpanDays) <= f.PeriodEnd
                && f.PeriodStart.AddDays(FiscalPeriodSpanDays.MaxAnnualSpanDays) >= f.PeriodEnd
                && f.DimensionsKey != ""
                && f.Dimensions.Count(d => RevenueBreakdownCore.AllAxes.Contains(d.Axis)) == 2
                && f.Dimensions.All(d =>
                    RevenueBreakdownCore.AllAxes.Contains(d.Axis)
                    || (
                        d.Axis == RevenueBreakdownCore.ConsolidationItemsAxis
                        && d.Member == RevenueBreakdownCore.OperatingSegmentsMember
                    )
                )
            )
            .Select(f => new
            {
                f.PeriodEnd,
                f.Value,
                f.Unit,
                f.FiledDate,
                f.PeriodStart,
                f.FiscalYear,
                Dims = f
                    .Dimensions.Where(d => RevenueBreakdownCore.AllAxes.Contains(d.Axis))
                    .Select(d => new { d.Axis, d.Member })
                    .ToList(),
            })
            .ToListAsync();
        return facts
            .Where(f => f.Dims.Count == 2)
            .Select(f => new CrossCutRevenueRow(
                f.Dims[0].Axis,
                f.Dims[0].Member,
                f.Dims[1].Axis,
                f.Dims[1].Member,
                f.PeriodEnd,
                f.Value,
                f.Unit,
                f.FiledDate,
                f.PeriodStart,
                f.FiscalYear
            ))
            .ToList();
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

        var rows = await LoadSingleAxisRows(stock, conceptIds, RevenueBreakdownCore.SegmentAxes);
        if (rows.Count == 0)
            return false;

        var consolidated = await _financialFactRepository
            .GetConsolidatedByStock(stock)
            .Where(f =>
                conceptIds.Contains(f.FinancialConceptId)
                && f.PeriodType == FactPeriodType.Duration
                && f.PeriodStart.AddDays(FiscalPeriodSpanDays.MinAnnualSpanDays) <= f.PeriodEnd
                && f.PeriodStart.AddDays(FiscalPeriodSpanDays.MaxAnnualSpanDays) >= f.PeriodEnd
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

        var incomeSeries = RevenueBreakdownCore.BuildAxisSeries(
            rows,
            RevenueBreakdownCore.SegmentAxes,
            years,
            totals
        );
        if (incomeSeries.Members.Count == 0)
            return false;

        AppendAxis(
            result,
            "Segment operating income",
            rows,
            RevenueBreakdownCore.SegmentAxes,
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

        var revenueSeries = RevenueBreakdownCore.BuildAxisSeries(
            revenueRows,
            RevenueBreakdownCore.SegmentAxes,
            years,
            revenueTotals
        );
        var marginSeries = RevenueBreakdownCore.BuildSegmentMarginSeries(
            revenueSeries,
            incomeSeries
        );
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
        var series = RevenueBreakdownCore.BuildAxisSeries(rows, axes, maxYears, totals);
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
        // axis (XOM's Canada highlight inside Non-US; NVDA's Data Center next to
        // Compute/Networking). Those rows survive the disjoint-scheme collapse
        // because the schemes share or nest members, so the column sums to well
        // over consolidated revenue — say so rather than let a consumer
        // double-count.
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
}
