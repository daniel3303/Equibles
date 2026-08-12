using System.ComponentModel;
using System.Globalization;
using System.Numerics;
using System.Text;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.CommonStocks.Repositories.Extensions;
using Equibles.CorporateActions.Data;
using Equibles.CorporateActions.Data.Models;
using Equibles.CorporateActions.Repositories;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.BusinessLogic.Extensions;
using Equibles.Errors.Data.Models;
using Equibles.Holdings.BusinessLogic;
using Equibles.Holdings.BusinessLogic.Models;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Repositories;
using Equibles.Holdings.Repositories.Extensions;
using Equibles.Holdings.Repositories.Models;
using Equibles.Mcp;
using Equibles.Mcp.Extensions;
using Equibles.Mcp.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace Equibles.Holdings.Mcp.Tools;

[McpServerToolType]
public class InstitutionalHoldingsTools
{
    private static readonly TimeSpan MarketActivityCacheDuration = TimeSpan.FromMinutes(30);

    private static readonly string[] ValidActivityBuckets =
    [
        "top-buys",
        "top-sells",
        "new-positions",
        "sold-out-positions",
    ];

    private static readonly string[] ValidInstitutionActivityBuckets =
    [
        "initiated",
        "increased",
        "reduced",
        "exited",
    ];

    private static readonly string[] ValidMostHeldSorts =
    [
        "filers",
        "filersdelta",
        "filersdeltaasc",
        "value",
    ];

    private readonly InstitutionalHoldingRepository _holdingRepository;
    private readonly InstitutionalHolderRepository _holderRepository;
    private readonly CommonStockRepository _commonStockRepository;
    private readonly StockSplitRepository _stockSplitRepository;
    private readonly StockCombinedQuarterService _combinedQuarterService;
    private readonly HoldingsCorpusCoverage _corpusCoverage;
    private readonly IMemoryCache _memoryCache;
    private readonly MarketActivityShareRestater _marketActivityShareRestater;
    private readonly InstitutionPortfolioSummaryProvider _summaryProvider;
    private readonly McpToolRunner _runner;

    public InstitutionalHoldingsTools(
        InstitutionalHoldingRepository holdingRepository,
        InstitutionalHolderRepository holderRepository,
        CommonStockRepository commonStockRepository,
        StockSplitRepository stockSplitRepository,
        StockCombinedQuarterService combinedQuarterService,
        ErrorManager errorManager,
        ILogger<InstitutionalHoldingsTools> logger,
        HoldingsCorpusCoverage corpusCoverage = null,
        IMemoryCache memoryCache = null,
        InstitutionPortfolioSummaryProvider summaryProvider = null,
        MarketActivityShareRestater marketActivityShareRestater = null
    )
    {
        _holdingRepository = holdingRepository;
        _holderRepository = holderRepository;
        _commonStockRepository = commonStockRepository;
        _stockSplitRepository = stockSplitRepository;
        _combinedQuarterService = combinedQuarterService;
        _corpusCoverage = corpusCoverage ?? HoldingsCorpusCoverage.Default;
        _memoryCache = memoryCache ?? new MemoryCache(new MemoryCacheOptions());
        _marketActivityShareRestater =
            marketActivityShareRestater
            ?? new MarketActivityShareRestater(_commonStockRepository, _stockSplitRepository);
        _summaryProvider =
            summaryProvider
            ?? new InstitutionPortfolioSummaryProvider(_holdingRepository, _memoryCache);
        _runner = new McpToolRunner(logger, errorManager.AsMcpErrorReporter());
    }

    [McpServerTool(Name = "GetTopHolders", Title = "Top Institutional Holders", ReadOnly = true)]
    [Description(
        "Get the top institutional holders (fund managers) of a stock from SEC 13F-HR filings. Returns a ranked list of institutions by shares held, including market value and percentage of total institutional 13F shares (not of shares outstanding). Data is sourced from quarterly 13F filings that large investment managers are required to file with the SEC; while the newest quarter's filing window is open, funds that have not filed yet are carried at their prior-quarter positions (noted in the output). Use this to understand who the major institutional investors in a company are."
    )]
    public Task<string> GetTopHolders(
        [Description("Company ticker symbol (e.g., AAPL, MSFT)")] string ticker,
        [Description(
            "Quarter-end 13F report date in YYYY-MM-DD format, e.g. 2026-03-31 (defaults to the latest available; an off-quarter date snaps to the nearest report on or before it)"
        )]
            string reportDate = null,
        [Description("Maximum number of holders to return (default: 20, clamped to 1-500)")]
            int maxResults = 20
    )
    {
        return _runner.Execute(
            async () =>
            {
                var (stock, stockError) = await _commonStockRepository.ResolveByTicker(ticker);
                if (stockError != null)
                    return stockError;

                var reportDates = await _holdingRepository.Get13FReportDatesByStockSnapshotBacked(
                    stock
                );
                if (reportDates.Count == 0)
                    return $"No institutional holdings data available for {ticker}.";

                var (targetDate, dateNote, dateError) = ResolveReportDateStrict(
                    reportDate,
                    reportDates
                );
                if (dateError != null)
                    return dateError;

                // While the newest quarter's filing window is open it only holds the early
                // filers, so it is presented as the combined view (carry-forward for funds
                // yet to file) — the same rule every web surface applies.
                var anchor = await _combinedQuarterService.Resolve(stock);
                var presentCombined =
                    anchor is { IsCombined: true } && targetDate == anchor.ReportDate;
                var allHoldings = presentCombined
                    ? _holdingRepository.GetCombinedQuarterByStockWithHolder(
                        stock,
                        anchor.ReportDate,
                        RequirePreviousReportDate(anchor)
                    )
                    : _holdingRepository.Get13FByStockWithHolder(stock, targetDate);
                // Materialise one compact projection. Exact-listing split factors can change
                // both rank and denominator, so a separate raw aggregate/page query would scan
                // the combined-quarter view twice and could rank a sibling class incorrectly.
                var holdings = await allHoldings
                    .AsNoTracking()
                    .Select(h => new TopHolderRow
                    {
                        Id = h.Id,
                        InstitutionalHolderId = h.InstitutionalHolderId,
                        InstitutionName = h.InstitutionalHolder.Name,
                        Shares = h.Shares,
                        Value = h.Value,
                        ReportDate = h.ReportDate,
                        ListedTicker = h.ListedTicker,
                        OptionType = h.OptionType,
                    })
                    .ToListAsync();
                if (holdings.Count == 0)
                    return $"No institutional holdings found for {ticker} as of {FormatDate(targetDate)}.";

                var splits = await _stockSplitRepository.GetByStock(stock.Id).ToListAsync();
                foreach (var holding in holdings)
                {
                    var listing = holding.ListedTicker ?? stock.Ticker;
                    holding.Shares = SplitAdjustment.AdjustShareCount(
                        holding.Shares,
                        holding.ReportDate,
                        PriceSeriesSplitScope.ForListing(splits, stock.Ticker, listing)
                    );
                }

                var totalInstitutions = holdings
                    .Select(h => h.InstitutionalHolderId)
                    .Distinct()
                    .Count();
                var totalShares = holdings.Sum(h => h.Shares);
                var totalValue = holdings.Sum(h => h.Value);
                maxResults = McpLimit.Clamp(maxResults);
                holdings = holdings
                    .OrderByDescending(h => h.Shares)
                    .ThenBy(h => h.Id)
                    .Take(maxResults)
                    .ToList();

                return RenderAdjustedTopHoldersTable(
                    stock,
                    ticker,
                    targetDate,
                    totalInstitutions,
                    totalShares,
                    totalValue,
                    holdings,
                    JoinNotes(
                        dateNote,
                        presentCombined ? CombinedViewNote(targetDate, anchor) : null
                    )
                );
            },
            "GetTopHolders",
            $"ticker: {ticker}"
        );
    }

    // Joins the optional per-call annotation lines (report-date substitution, combined view,
    // name-match diagnostics) into one block, dropping the nulls, so render sites can pass a
    // single nullable note.
    private static string JoinNotes(params string[] notes)
    {
        var present = notes.Where(n => !string.IsNullOrEmpty(n)).ToList();
        return present.Count == 0 ? null : string.Join("\n", present);
    }

    // The one wording every combined-view tool output carries, so agents and users always see
    // WHY the newest quarter is presented as a merge of two filing sets.
    private static string CombinedViewNote(DateOnly targetDate, StockQuarterAnchor anchor) =>
        $"Note: the {FormatDate(targetDate)} filing window is still open (13Fs are due 45 days "
        + $"after quarter end). Combined view: funds that have not filed yet carry their "
        + $"{FormatDate(RequirePreviousReportDate(anchor))} positions.";

    private static DateOnly RequirePreviousReportDate(StockQuarterAnchor anchor) =>
        anchor.PreviousReportDate
        ?? throw new InvalidOperationException(
            "A combined-quarter anchor must include its previous report date."
        );

    // Stable pure rendering seam retained for the culture/date/zero-denominator contracts.
    // The request path above uses exact-listing adjusted rows because sibling classes can
    // have different split factors; this adapter preserves the former single-factor shape.
    private static string RenderTopHoldersTable(
        CommonStock stock,
        string ticker,
        DateOnly targetDate,
        int totalInstitutions,
        long totalSharesAll,
        long totalValueAll,
        List<InstitutionalHolding> holdings,
        decimal shareFactor,
        string combinedNote
    )
    {
        var adjustedTotalShares = SplitAdjustment.AdjustShareCount(totalSharesAll, shareFactor);
        var adjustedRows = holdings
            .Select(h => new TopHolderRow
            {
                Id = h.Id,
                InstitutionalHolderId = h.InstitutionalHolderId,
                InstitutionName = h.InstitutionalHolder?.Name ?? "Unknown",
                Shares = SplitAdjustment.AdjustShareCount(h.Shares, shareFactor),
                Value = h.Value,
                ReportDate = h.ReportDate,
                ListedTicker = h.ListedTicker,
                OptionType = h.OptionType,
            })
            .ToList();
        return RenderAdjustedTopHoldersTable(
            stock,
            ticker,
            targetDate,
            totalInstitutions,
            adjustedTotalShares,
            totalValueAll,
            adjustedRows,
            combinedNote
        );
    }

    private static string RenderAdjustedTopHoldersTable(
        CommonStock stock,
        string ticker,
        DateOnly targetDate,
        int totalInstitutions,
        long totalSharesAll,
        long totalValueAll,
        List<TopHolderRow> holdings,
        string combinedNote
    )
    {
        var subtitle =
            $"Showing {holdings.Count} of {totalInstitutions} institutions. Total: "
            + $"{McpFormat.WholeNumber(totalSharesAll)} shares, "
            + $"${FormatMillions(totalValueAll)}M value";
        if (combinedNote != null)
            subtitle = $"{subtitle}\n{combinedNote}";
        var result = MarkdownTable.Start(
            $"Top institutional holders of {stock.Name} ({ticker}) as of {FormatDate(targetDate)}:",
            subtitle,
            "| # | Institution | Type | Shares | Value ($M) | % of Inst. Total |",
            "|---|------------|------|--------|-----------|-----------|"
        );

        result.AppendNumberedRows(
            holdings,
            (rank, h) =>
            {
                var pct = Percentage.Of(h.Shares, totalSharesAll);
                // A sibling-class row is a DIFFERENT security of the same issuer; the class
                // qualifies the type in place so single-class stocks pay no extra column.
                var positionType =
                    h.ListedTicker == null
                        ? PositionType(h.OptionType)
                        : $"{PositionType(h.OptionType)} ({h.ListedTicker})";
                return $"| {rank} | {h.InstitutionName} | "
                    + $"{positionType} | "
                    + $"{McpFormat.WholeNumber(h.Shares)} | "
                    + $"{FormatMillions(h.Value)} | "
                    + $"{McpFormat.Invariant(pct, "F2")}% |";
            }
        );

        result.AppendLine();
        result.AppendLine(
            "_% of Inst. Total = the position's share of all institutional 13F shares in the stock, not of shares outstanding._"
        );
        result.AppendLine(
            "_Type: Common = shares held outright. Put/Call = an option position reported at the "
                + "underlying's notional value. A PUT IS A BEARISH POSITION, so a large put line is a "
                + "holder betting against this stock, not accumulating it._"
        );

        return result.ToString();
    }

    private sealed class TopHolderRow
    {
        public Guid Id { get; set; }
        public Guid InstitutionalHolderId { get; set; }
        public string InstitutionName { get; set; }
        public long Shares { get; set; }
        public long Value { get; set; }
        public DateOnly ReportDate { get; set; }
        public string ListedTicker { get; set; }
        public OptionType? OptionType { get; set; }
    }

    [McpServerTool(
        Name = "GetOwnershipHistory",
        Title = "Institutional Ownership History",
        ReadOnly = true
    )]
    [Description(
        "Get the historical trend of institutional ownership for a stock across multiple quarters. Shows how total institutional shares, market value, and number of institutional holders have changed over time based on SEC 13F-HR filings. While the newest quarter's 13F filing window is open, that quarter is a provisional combined view (funds that have not filed yet carry their prior-quarter positions — flagged in the output). Use this to understand whether institutional interest in a company is growing or declining."
    )]
    public Task<string> GetOwnershipHistory(
        [Description("Company ticker symbol (e.g., AAPL, MSFT)")] string ticker,
        [Description(
            "Maximum number of quarterly periods to return (default: 8, clamped to 1-500)"
        )]
            int maxPeriods = 8
    )
    {
        return _runner.Execute(
            async () =>
            {
                var (stock, stockError) = await _commonStockRepository.ResolveByTicker(ticker);
                if (stockError != null)
                    return stockError;

                var activity =
                    await _holdingRepository.GetStockActivitySnapshotsByStockSnapshotBacked(stock);
                if (activity.All(row => row.CurrentFilerCount <= 0))
                    return $"No institutional holdings history available for {ticker}.";

                var anchor = await _combinedQuarterService.Resolve(stock);
                if (anchor is { IsCombined: true })
                {
                    var combined = await _holdingRepository.GetCombinedStockActivitySnapshotBacked(
                        stock,
                        anchor.ReportDate,
                        RequirePreviousReportDate(anchor)
                    );
                    if (combined == null)
                    {
                        throw new InvalidOperationException(
                            $"Combined holdings activity is unavailable for {stock.Ticker} on {anchor.ReportDate:yyyy-MM-dd}."
                        );
                    }

                    var index = activity.FindIndex(row => row.ReportDate == anchor.ReportDate);
                    if (index >= 0)
                        activity[index] = combined;
                    else
                        activity.Add(combined);
                }

                var selected = activity
                    .Where(row => row.CurrentFilerCount > 0)
                    .OrderByDescending(row => row.ReportDate)
                    .Take(McpLimit.Clamp(maxPeriods))
                    .OrderBy(row => row.ReportDate)
                    .ToList();
                await _marketActivityShareRestater.RestateStockActivity(stock, selected);
                return RenderOwnershipHistory(stock, ticker, selected, anchor);
            },
            "GetOwnershipHistory",
            $"ticker: {ticker}"
        );
    }

    private static string RenderOwnershipHistory(
        CommonStock stock,
        string ticker,
        IReadOnlyList<StockQuarterlyActivity> activity,
        StockQuarterAnchor anchor
    )
    {
        var result = MarkdownTable.Start(
            $"Institutional ownership history for {stock.Name} ({ticker}):",
            "| Report Date | Institutions | Total Shares | Total Value ($M) | Share Chg (QoQ) |",
            "|------------|-------------|-------------|-----------------|--------|"
        );

        long previousShares = 0;
        var combinedRowShown = false;
        foreach (var row in activity)
        {
            var isCombinedRow =
                anchor is { IsCombined: true } && row.ReportDate == anchor.ReportDate;
            combinedRowShown |= isCombinedRow;
            var change = FormatShareChange(row.CurrentShares, previousShares);

            result.AppendLine(
                $"| {FormatDate(row.ReportDate)}{(isCombinedRow ? " \\*" : "")} | {McpFormat.WholeNumber(row.CurrentFilerCount)} | {McpFormat.WholeNumber(row.CurrentShares)} | {FormatMillions(row.CurrentValue)} | {change} |"
            );

            previousShares = row.CurrentShares;
        }

        if (combinedRowShown)
        {
            result.AppendLine();
            result.AppendLine($"\\* {CombinedViewNote(anchor.ReportDate, anchor)}");
        }

        result.AppendLine();
        result.AppendLine(
            "_Share Chg (QoQ) tracks the quarter-over-quarter change in total split-adjusted institutional shares._"
        );

        return result.ToString();
    }

    // Quarter-over-quarter share change. The three-section format is load-bearing: a negative
    // change that rounds to zero re-formats through the zero section as "0.0", where the
    // two-section "+0.0;-0.0" form emitted the garbled "-+0.0" (negative-zero double keeps its
    // sign when re-formatted through the positive section) — which hit almost every combined
    // current-quarter row, whose carried-forward share change is near-zero by construction.
    private static string FormatShareChange(long totalShares, long previousShares) =>
        previousShares > 0
            ? $"{McpFormat.Invariant((double)(totalShares - previousShares) / previousShares * 100, "+0.0;-0.0;0.0")}%"
            : "—";

    [McpServerTool(
        Name = "GetInstitutionPortfolio",
        Title = "Institution Portfolio (13F)",
        ReadOnly = true
    )]
    [Description(
        "View the stock portfolio of a specific institutional investor (fund manager) from their SEC 13F-HR filing. Shows the institution's largest tracked holdings by market value (default 20, max 500) with share counts, market values, and percent of the 13F-reported portfolio, plus the portfolio's total value and position count. Use this to understand what stocks a particular fund manager or institution is investing in; use SearchInstitutions first when the name is ambiguous."
    )]
    public Task<string> GetInstitutionPortfolio(
        [Description(
            "Institution name or SEC CIK. A unique partial name resolves; an ambiguous partial returns candidate CIKs instead of selecting silently."
        )]
            string institutionName,
        [Description(
            "Quarter-end 13F report date in YYYY-MM-DD format (defaults to the holder's latest; an off-quarter date snaps to the nearest report on or before it)"
        )]
            string reportDate = null,
        [Description("Maximum number of holdings to return (default: 20, clamped to 1-500)")]
            int maxResults = 20,
        [Description(
            "Number of ranked holding rows to skip before returning rows — pass the previous call's last row number to page past the maxResults cap (default: 0)"
        )]
            int offset = 0
    )
    {
        return _runner.Execute(
            async () =>
            {
                var (holder, matchNote, holderError) = await ResolveHolderByName(institutionName);
                if (holderError != null)
                    return holderError;

                var reportDates = await _holdingRepository.Get13FReportDatesByHolderSnapshotBacked(
                    holder
                );
                if (reportDates.Count == 0)
                    return $"No holdings data for {holder.Name}.";

                var (targetDate, dateNote, dateError) = ResolveReportDateStrict(
                    reportDate,
                    reportDates
                );
                if (dateError != null)
                    return dateError;

                var allHoldings = _holdingRepository.Get13FByHolderWithStock(holder, targetDate);

                // One grouped round trip for the three header scalars — this is the same table
                // whose non-index-only scans once drained a connection pool, so the extra
                // per-call queries matter. The unvalued count shares the header's distinct-stock
                // basis (a filer's option and share rows must not double-count), and its
                // predicate must catch every cohort of $0 rows: still pending, marked
                // unknowable, AND rows the old retry ladder abandoned with neither flag set —
                // those carry only the filer's own FiledValue as the tell. Counting just the
                // flagged rows once reported "1 unvalued position" for a filer with 94 of its 96
                // positions at $0 — the same dishonesty this disclosure exists to remove.
                var summary = await allHoldings
                    .GroupBy(h => 1)
                    .Select(g => new
                    {
                        // Rows and Positions differ: a stock held as shares AND as puts/calls
                        // is one distinct position spread over several holding rows, and the
                        // table below renders ROWS — quoting the distinct-stock count as its
                        // denominator once produced "top 39 of 35".
                        Rows = g.Count(),
                        Positions = g.Select(h => h.CommonStockId).Distinct().Count(),
                        Value = g.Sum(h => h.Value),
                        Unvalued = g.Where(h =>
                                h.Value == 0L
                                && (h.ValuePending || h.ValueUnavailable || h.FiledValue > 0)
                            )
                            .Select(h => h.CommonStockId)
                            .Distinct()
                            .Count(),
                    })
                    .FirstOrDefaultAsync();
                var totalRows = summary?.Rows ?? 0;
                var totalPositions = summary?.Positions ?? 0;
                var totalValue = summary?.Value ?? 0L;
                var unvaluedPositions = summary?.Unvalued ?? 0;

                // What the filer itself declared for the quarter, from the latest filing's cover
                // page — so the answer can say "7 of the 8 declared positions" instead of
                // presenting the tracked subset as the whole filing. Null on rows ingested
                // before declared totals were captured; the renderer then says only "tracked".
                var declaringFiling = await _holdingRepository
                    .GetFilingsByHolder(holder, targetDate)
                    .OrderByDescending(f => f.FilingDate)
                    .ThenByDescending(f => f.AccessionNumber)
                    .FirstOrDefaultAsync();

                offset = McpLimit.ClampOffset(offset);
                // Values tie (equal-sized lines, $0 unvalued rows), so the ordering ends on
                // the row id — an offset over a partial order would silently repeat or skip
                // rows between pages.
                var holdings = await allHoldings
                    .OrderByDescending(h => h.Value)
                    .ThenBy(h => h.CommonStockId)
                    .ThenBy(h => h.Id)
                    .Skip(offset)
                    .Take(McpLimit.Clamp(maxResults))
                    .ToListAsync();

                if (holdings.Count == 0 && offset > 0)
                    return $"No results at offset {offset} - only {totalRows} holding rows on file for {holder.Name} as of {FormatDate(targetDate)}; lower offset.";
                if (holdings.Count == 0)
                    return $"No holdings found for {holder.Name} as of {FormatDate(targetDate)}.";

                // 13F holdings are shown on today's split basis across the platform (matching the
                // web and GetTopHolders), so restate each position's share count by its own
                // stock's post-report-date splits. Value is a paired per-holding dollar figure
                // and stays as reported.
                var splitsByStock = await LoadSplitsByStock(holdings.Select(h => h.CommonStockId));

                return RenderInstitutionPortfolio(
                    holder,
                    targetDate,
                    holdings,
                    splitsByStock,
                    offset,
                    totalRows,
                    totalPositions,
                    totalValue,
                    unvaluedPositions,
                    declaringFiling,
                    JoinNotes(matchNote, dateNote)
                );
            },
            "GetInstitutionPortfolio",
            $"institution: {institutionName}, offset: {offset}"
        );
    }

    private static string RenderInstitutionPortfolio(
        InstitutionalHolder holder,
        DateOnly targetDate,
        List<InstitutionalHolding> holdings,
        IReadOnlyDictionary<Guid, List<StockSplit>> splitsByStock,
        int offset,
        int totalRows,
        int totalPositions,
        long totalValue,
        int unvaluedPositions,
        InstitutionalFiling declaringFiling,
        string notes
    )
    {
        // The table renders holding ROWS, so the coverage line counts rows — quoting the
        // distinct-stock count as the denominator once produced "top 39 of 35" for a filer
        // whose stocks appear as separate share and option rows.
        var subtitle =
            $"Showing holding rows {offset + 1}-{offset + holdings.Count} of {McpFormat.WholeNumber(totalRows)}, "
            + $"largest value first ({McpFormat.WholeNumber(totalPositions)} distinct tracked stocks — a stock "
            + "held as shares and as options appears as separate rows). "
            + $"Tracked 13F value: ${FormatMillions(totalValue)}M";

        // The filer's own cover-page declaration, when captured, makes the coverage exact: the
        // reader learns how many positions the filing carries in total and what they are worth,
        // so a tracked subset can never read as the whole filing. The declared-vs-tracked gap has
        // TWO causes and blaming coverage for both once hid real top holdings — unvalued rows are
        // named separately below.
        if (
            declaringFiling
            is { DeclaredPositionCount: not null }
                or { DeclaredTotalValue: not null }
        )
        {
            var declaredParts = new List<string>();
            if (declaringFiling.DeclaredPositionCount is { } declaredCount)
                declaredParts.Add($"{McpFormat.WholeNumber(declaredCount)} positions");
            if (declaringFiling.DeclaredTotalValue is { } declaredValue)
                declaredParts.Add($"${FormatMillions(declaredValue)}M");
            // Unvalued rows are TRACKED — already inside the tracked position count — so they
            // explain none of the position-count gap; they explain part of the VALUE gap only.
            subtitle +=
                $" The filing itself declares {string.Join(" totalling ", declaredParts)}; "
                + (
                    unvaluedPositions > 0
                        ? "the position difference is security types outside this platform's "
                            + "coverage, and the value difference also reflects the unvalued "
                            + "positions noted below."
                        : "the difference is security types outside this platform's coverage."
                );
        }

        // Rows at $0 are not "no position" — they are tracked positions with no derivable dollar
        // value yet. Say so, or the total and every percentage read as the whole story.
        if (unvaluedPositions > 0)
        {
            subtitle +=
                $" NOTE: {McpFormat.WholeNumber(unvaluedPositions)} tracked position(s) have no "
                + "derivable value and count as $0 in the total above, so the total and the "
                + "percentages understate the filing.";
        }
        if (notes != null)
            subtitle = $"{subtitle}\n{notes}";

        var result = MarkdownTable.Start(
            $"Portfolio of {holder.Name} (CIK: {holder.Cik}) as of {FormatDate(targetDate)}:",
            subtitle,
            "| # | Ticker | Company | Type | Shares | Value ($M) | % of Portfolio |",
            "|---|--------|---------|------|--------|-----------|----------------|"
        );

        result.AppendNumberedRows(
            holdings,
            (rank, h) =>
            {
                var shares = SplitAdjustment.AdjustShareCount(
                    h.Shares,
                    targetDate,
                    SplitsFor(splitsByStock, h.CommonStockId)
                );
                var pct = Percentage.Of(h.Value, totalValue);
                // The exact listing held: a GOOG position must not render as GOOGL.
                var listedTicker = h.ListedTicker ?? h.CommonStock.Ticker;
                // Rank is the ABSOLUTE position in the value-ranked rows, so page two
                // continues 21, 22, … instead of restarting at 1.
                return $"| {offset + rank} | {listedTicker} | {h.CommonStock.Name} | "
                    + $"{PositionType(h.OptionType)} | "
                    + $"{McpFormat.WholeNumber(shares)} | "
                    + $"{FormatMillions(h.Value)} | "
                    + $"{FormatPercent(pct)}% |";
            }
        );

        // A 13F reports option positions beside common stock, and a put is a bet AGAINST the
        // issuer. Without this the table reads as a long book: Scion's Q3 2025 filing shows a
        // 5,000,000-share Palantir line at 67% of the portfolio, which is a put — Burry was short
        // Palantir, and every surface that omitted the distinction reported the opposite.
        result.AppendLine();
        result.AppendLine(
            "_Type: Common = shares held outright. Put/Call = an option position, reported at the "
                + "notional value of the underlying shares, not the premium paid. A PUT IS A BEARISH "
                + "POSITION — the filer profits if the stock falls. Option notional is included in the "
                + "portfolio total and the percentages above, exactly as the filer reported it._"
        );
        result.AppendLine(
            "_Coverage: totals span the U.S.-listed common stock (and its put/call positions) "
                + "this platform tracks. A 13F can also report security types outside that "
                + "coverage — preferred shares, bonds, warrants, untracked share classes — so the "
                + "filing's own declared total can exceed the figure above._"
        );

        var pagedNote = McpOutput.PagedTruncationNote(holdings.Count, totalRows, offset);
        if (pagedNote.Length > 0)
        {
            result.AppendLine();
            result.AppendLine(pagedNote);
        }

        return result.ToString();
    }

    // How a 13F line should be described to a reader: the security type, not the activity bucket.
    private static string PositionType(OptionType? optionType) =>
        optionType switch
        {
            Equibles.Holdings.Data.Models.OptionType.Put => "Put",
            Equibles.Holdings.Data.Models.OptionType.Call => "Call",
            _ => "Common",
        };

    [McpServerTool(
        Name = "SearchInstitutions",
        Title = "Search Institutional Investors",
        ReadOnly = true
    )]
    [Description(
        "Search the tracked 13F filer set by institution name or SEC CIK. Search first requires every punctuation-independent query word anywhere in the filed name, then broadens to any word only when no strict row matches. Verified brand aliases such as Fidelity, Vanguard, and BlackRock include their current flagship CIK. Results are largest within the recently-active filing bucket first and include latest report date, reported 13F AUM, and tracked position count so same-name filers can be compared before calling an institution tool. Scoped institution tools remain strict and never discard an unmatched word."
    )]
    public Task<string> SearchInstitutions(
        [Description("Search query — institution name, partial name, or CIK")] string query,
        [Description("Maximum number of results to return (default: 10, clamped to 1-500)")]
            int maxResults = 10
    )
    {
        return _runner.Execute(
            async () =>
            {
                maxResults = McpLimit.Clamp(maxResults);

                var totalMatches = await _holderRepository.SearchNameOrCik(query).CountAsync();
                if (totalMatches == 0)
                    return $"No match for '{query}' in the tracked 13F filer set. This result describes only tracked filers.";

                var holders = await _holderRepository.SearchNameOrCikLargestFirstWithStats(
                    query,
                    maxResults
                );

                var table = MarkdownTable.Render(
                    holders,
                    $"No match for '{query}' in the tracked 13F filer set. This result describes only tracked filers.",
                    $"Institutions matching '{query}' (largest recently-active 13F filers first):",
                    "| Institution | CIK | Latest Report | Reported AUM | Positions | City | State/Country |",
                    "|------------|-----|---------------|--------------|-----------|------|--------------|",
                    h =>
                        $"| {MarkdownTable.EscapeCell(h.Holder.Name, "—")} | {MarkdownTable.EscapeCell(h.Holder.Cik, "—")} | {FormatOptionalDate(h.LatestReportDate)} | {FormatOptionalDollars(h.ReportedAum)} | {FormatOptionalCount(h.PositionCount)} | {MarkdownTable.EscapeCell(OrDash(h.Holder.City), "—")} | {MarkdownTable.EscapeCell(OrDash(EdgarStateCodes.Decode(h.Holder.StateOrCountry)), "—")} |"
                );

                var truncation = McpOutput.TruncationNote(holders.Count, totalMatches);
                var notes = new List<string>();
                if (truncation.Length > 0)
                    notes.Add(truncation);
                if (totalMatches > 1)
                {
                    notes.Add(
                        "_Separate SEC CIKs can represent current, predecessor, or otherwise distinct registrants. An older CIK may carry the longer history; compare Latest Report and use the exact CIK in institution-scoped tools._"
                    );
                }

                return notes.Count == 0 ? table : $"{table}\n{string.Join("\n", notes)}";
            },
            "SearchInstitutions",
            $"query: {query}"
        );
    }

    // Empty strings occur in the location columns alongside NULLs (importer stores what EDGAR
    // sends), so a bare null-coalesce still rendered blank cells instead of the placeholder.
    private static string OrDash(string value) => string.IsNullOrWhiteSpace(value) ? "—" : value;

    private static string FormatOptionalDate(DateOnly? value) =>
        value == null ? "—" : McpFormat.Invariant(value.Value, "yyyy-MM-dd");

    private static string FormatOptionalDollars(long? value) =>
        value == null ? "—" : $"${McpFormat.WholeNumber(value.Value)}";

    private static string FormatOptionalCount(int? value) =>
        value == null ? "—" : McpFormat.WholeNumber(value.Value);

    [McpServerTool(
        Name = "GetTopBuyersSellers",
        Title = "Top Institutional Buyers and Sellers",
        ReadOnly = true
    )]
    [Description(
        "Get the institutions that moved the needle the most on a stock this quarter — biggest absolute share additions (Top Buyers) and biggest absolute share reductions (Top Sellers) versus the previous 13F report date. Includes new positions (Δ = full position) and sold-out positions (Δ = −prior position); a previous holder counts as a seller only if it filed a 13F for the target quarter, so a fund that stopped filing (CIK migration, deregistration) is not shown as a mass seller. While the newest quarter's filing window is open, results cover only the funds that have already filed (noted in the output). Returns a markdown table with two sections. Use this to surface the most actionable quarterly signal from 13F filings."
    )]
    public Task<string> GetTopBuyersSellers(
        [Description("Company ticker symbol (e.g., AAPL, MSFT)")] string ticker,
        [Description(
            "Quarter-end 13F report date in YYYY-MM-DD format, e.g. 2026-03-31 (defaults to the latest available; an off-quarter date snaps to the nearest report on or before it)"
        )]
            string reportDate = null,
        [Description(
            "Maximum number of buyers and sellers to return per section (default: 10, clamped to 1-500)"
        )]
            int maxResults = 10
    )
    {
        return _runner.Execute(
            async () =>
            {
                maxResults = McpLimit.Clamp(maxResults);

                var (stock, stockError) = await _commonStockRepository.ResolveByTicker(ticker);
                if (stockError != null)
                    return stockError;

                var reportDates = await _holdingRepository.Get13FReportDatesByStockSnapshotBacked(
                    stock
                );
                if (reportDates.Count == 0)
                    return $"No institutional holdings data available for {ticker}.";

                var (targetDate, dateNote, dateError) = ResolveReportDateStrict(
                    reportDate,
                    reportDates
                );
                if (dateError != null)
                    return dateError;

                var previousDate = GetPriorReportDate(reportDates, targetDate);
                var activity = await _holdingRepository
                    .Get13FHolderActivityByStock(stock, targetDate, previousDate)
                    .ToListAsync();

                // A previous holder with no current row only PROVES an exit when it filed a
                // 13F for the target quarter elsewhere. While the filing window is open the
                // missing row usually means "hasn't filed yet"; on closed quarters it usually
                // means the filer stopped filing under that CIK (entity migration,
                // deregistration) — Vanguard's CIK move would otherwise rank as a 2.3B-share
                // NVDA seller. Both cases restrict the comparison to REPORTERS: the quarter's
                // filers plus previous holders who filed elsewhere (proven exits).
                var windowOpen =
                    previousDate.HasValue
                    && targetDate == reportDates[0]
                    && CombinedQuarterHelper.IsFilingWindowOpen(targetDate);
                HashSet<Guid> filedPreviousHolders = null;
                if (previousDate.HasValue)
                {
                    var priorHolderIds = activity
                        .Where(row => row.PreviousPositionCount > 0)
                        .Select(row => row.InstitutionalHolderId)
                        .Distinct()
                        .ToList();
                    filedPreviousHolders = (
                        await _holdingRepository
                            .GetFiledHolderIdsAmong(targetDate, priorHolderIds)
                            .ToListAsync()
                    ).ToHashSet();
                }

                // Restate each quarter's share counts onto today's split basis (the two
                // quarters sit on different bases if a split fell between them) so Δ Shares
                // and the Prior → New column reflect a real position change, not the split.
                // Δ Value is a dollar figure and is split-invariant — leave the stored value.
                var splits = await _stockSplitRepository.GetByStock(stock.Id).ToListAsync();
                long AdjustShares(long shares, DateOnly asOf, string listedTicker)
                {
                    var exactTicker = listedTicker ?? stock.Ticker;
                    var scoped = PriceSeriesSplitScope.ForListing(
                        splits,
                        stock.Ticker,
                        exactTicker
                    );
                    return SplitAdjustment.AdjustShareCount(shares, asOf, scoped);
                }

                var movers = activity
                    .GroupBy(row => row.InstitutionalHolderId)
                    .Where(g =>
                        filedPreviousHolders == null
                        || g.Any(row => row.CurrentPositionCount > 0)
                        || filedPreviousHolders.Contains(g.Key)
                    )
                    .Select(g =>
                    {
                        var currentShares = g.Sum(row =>
                            AdjustShares(row.CurrentShares, targetDate, row.ListedTicker)
                        );
                        var previousShares = g.Sum(row =>
                            AdjustShares(
                                row.PreviousShares,
                                previousDate ?? targetDate,
                                row.ListedTicker
                            )
                        );
                        return (
                            Id: g.Key,
                            CurrentShares: currentShares,
                            PreviousShares: previousShares,
                            DeltaShares: currentShares - previousShares,
                            DeltaValue: g.Sum(row => row.CurrentValue - row.PreviousValue)
                        );
                    })
                    .ToList();

                var topBuyers = movers
                    .Where(m => m.DeltaShares > 0)
                    .OrderByDescending(m => m.DeltaShares)
                    .Take(maxResults)
                    .ToList();
                var topSellers = movers
                    .Where(m => m.DeltaShares < 0)
                    .OrderBy(m => m.DeltaShares)
                    .Take(maxResults)
                    .ToList();

                if (topBuyers.Count == 0 && topSellers.Count == 0)
                    return $"No quarter-over-quarter movement found for {stock.Name} ({ticker}) as of {FormatDate(targetDate)}.";

                var topHolderIds = topBuyers
                    .Select(m => m.Id)
                    .Concat(topSellers.Select(m => m.Id))
                    .Distinct()
                    .ToList();
                var holderNames = await _holderRepository
                    .GetAll()
                    .Where(h => topHolderIds.Contains(h.Id))
                    .Select(h => new { h.Id, h.Name })
                    .ToDictionaryAsync(h => h.Id, h => h.Name);
                string HolderName(Guid id) =>
                    holderNames.TryGetValue(id, out var name) ? name : "Unknown";

                // Flag first-time filers among whole-position buyers: a filer whose FIRST 13F
                // is the target quarter shows its entire book as "new positions", which is
                // often the receiving entity of a CIK migration rather than fresh buying.
                var firstTimeFilerIds = new HashSet<Guid>();
                var newPositionBuyerIds = topBuyers
                    .Where(m => m.PreviousShares == 0)
                    .Select(m => m.Id)
                    .ToList();
                if (newPositionBuyerIds.Count > 0)
                {
                    firstTimeFilerIds = (
                        await _holdingRepository
                            .GetEarliest13FReportDates(newPositionBuyerIds)
                            .ToListAsync()
                    )
                        .Where(kv => kv.Value == targetDate)
                        .Select(kv => kv.Key)
                        .ToHashSet();
                }

                var buyerRows = topBuyers
                    .Select(m =>
                        (
                            firstTimeFilerIds.Contains(m.Id)
                                ? $"{HolderName(m.Id)} (first 13F this quarter)"
                                : HolderName(m.Id),
                            m.CurrentShares,
                            m.PreviousShares,
                            m.DeltaShares,
                            m.DeltaValue
                        )
                    )
                    .ToList();
                var sellerRows = topSellers
                    .Select(m =>
                        (
                            HolderName(m.Id),
                            m.CurrentShares,
                            m.PreviousShares,
                            m.DeltaShares,
                            m.DeltaValue
                        )
                    )
                    .ToList();

                var comparisonNote =
                    windowOpen
                        ? $"Note: the {FormatDate(targetDate)} filing window is still open — "
                            + "movement is computed only across funds that have already filed."
                    : previousDate.HasValue
                        ? $"Note: sellers are counted only among funds that filed a 13F for {FormatDate(targetDate)} — "
                            + "funds that stopped filing under this CIK (migrations, deregistrations) are excluded."
                    : null;

                return RenderBuyersSellersTable(
                    stock,
                    ticker,
                    targetDate,
                    previousDate,
                    buyerRows,
                    sellerRows,
                    JoinNotes(dateNote, comparisonNote)
                );
            },
            "GetTopBuyersSellers",
            $"ticker: {ticker}"
        );
    }

    private static string RenderBuyersSellersTable(
        CommonStock stock,
        string ticker,
        DateOnly targetDate,
        DateOnly? previousDate,
        IReadOnlyList<(
            string Name,
            long CurrentShares,
            long PreviousShares,
            long DeltaShares,
            long DeltaValue
        )> topBuyers,
        IReadOnlyList<(
            string Name,
            long CurrentShares,
            long PreviousShares,
            long DeltaShares,
            long DeltaValue
        )> topSellers,
        string windowNote
    )
    {
        var result = new StringBuilder();
        result.AppendLine(
            $"Top buyers and sellers of {stock.Name} ({ticker}) as of {FormatDate(targetDate)}"
        );
        if (previousDate.HasValue)
            result.AppendLine(PriorQuarterSubtitle(previousDate.Value));
        if (windowNote != null)
            result.AppendLine(windowNote);
        result.AppendLine();

        AppendMoverSection(result, "## Top Buyers", "_No buyers this quarter._", topBuyers);
        result.AppendLine();
        AppendMoverSection(result, "## Top Sellers", "_No sellers this quarter._", topSellers);

        result.AppendLine();
        result.AppendLine(
            "_Δ Position Value is the change in stored quarter-end position value and includes price movement — a seller can show a positive Δ when the stock rose during the quarter._"
        );

        return result.ToString();

        static void AppendMoverSection(
            StringBuilder sb,
            string heading,
            string emptyMessage,
            IReadOnlyList<(
                string Name,
                long CurrentShares,
                long PreviousShares,
                long DeltaShares,
                long DeltaValue
            )> rows
        )
        {
            sb.AppendLine(heading);
            if (rows.Count == 0)
            {
                sb.AppendLine(emptyMessage);
                return;
            }
            sb.AppendNumberedTable(
                "| # | Institution | Δ Shares | Δ Position Value ($M) | Prior → New Shares |",
                "|---|-------------|---------|-------------|------------------|",
                rows,
                (rank, m) =>
                    $"| {rank} | {m.Name} | {FormatSignedShares(m.DeltaShares)} | {FormatSignedMillions(m.DeltaValue)} | {McpFormat.WholeNumber(m.PreviousShares)} → {McpFormat.WholeNumber(m.CurrentShares)} |"
            );
        }
    }

    [McpServerTool(
        Name = "GetMarketWide13FActivity",
        Title = "Market-Wide 13F Activity",
        ReadOnly = true
    )]
    [Description(
        "Get the market-wide 13F leaderboards for a given quarter — which stocks were most bought, most sold, most initiated, or most exited across all 13F filers vs the prior quarter. The `bucket` argument selects one of: top-buys (Δ shares > 0 ranked by Δ value desc), top-sells (Δ shares < 0 ranked by Δ value asc), new-positions (stocks ranked by count of filers initiating a position), sold-out-positions (stocks ranked by count of filers exiting). Δ Value is the change in stored quarter-end position value and includes the quarter's price move on held shares, so use Δ Shares to read the position change itself. The output publishes the first complete 13F report quarter and refuses comparisons that cross that corpus boundary. Use this to answer 'what's the consensus 13F move this quarter?'"
    )]
    public Task<string> GetMarketWide13FActivity(
        [Description("Bucket: top-buys, top-sells, new-positions, or sold-out-positions")]
            string bucket,
        [Description(
            "Quarter-end 13F report date in YYYY-MM-DD format, e.g. 2026-03-31 (defaults to the latest available 13F quarter; an off-quarter date snaps to the nearest report on or before it)"
        )]
            string reportDate = null,
        [Description("Maximum number of stocks to return (default: 20, clamped to 1-500)")]
            int maxResults = 20
    )
    {
        return _runner.Execute(
            async () =>
            {
                var normalizedBucket = (bucket ?? string.Empty).Trim().ToLowerInvariant();
                if (!ValidActivityBuckets.Contains(normalizedBucket))
                    return $"Unknown bucket. Use one of: {string.Join(", ", ValidActivityBuckets)}.";

                maxResults = McpLimit.Clamp(maxResults);

                var (targetDate, previousDate, windowOpen, dateNote, coverage, error) =
                    await ResolveMarketActivityDates(reportDate);
                if (error != null)
                    return error;

                if (!coverage.IsWithinCoverage || !coverage.ComparisonAvailable)
                {
                    return $"Market-wide 13F {normalizedBucket} for {FormatDate(targetDate)}\n"
                        + $"_No ranking is available. {coverage.ComparisonUnavailableReason}_";
                }

                var comparisonDate = previousDate.Value;

                // Headline + comparison subtitle.
                var result = new StringBuilder();
                result.AppendLine(
                    $"Market-wide 13F **{normalizedBucket}** for {FormatDate(targetDate)}"
                );
                result.AppendLine(PriorQuarterSubtitle(comparisonDate));
                result.AppendLine(
                    $"Complete 13F coverage begins {FormatDate(coverage.CoverageStartDate)}"
                );
                if (dateNote != null)
                    result.AppendLine(dateNote);
                if (windowOpen)
                    result.AppendLine(
                        $"Note: the {FormatDate(targetDate)} filing window is still open — "
                            + "figures cover only the funds that have already filed."
                    );
                result.AppendLine();

                if (normalizedBucket is "top-buys" or "top-sells")
                {
                    return await RenderMarketActivityMovers(
                        normalizedBucket,
                        targetDate,
                        comparisonDate,
                        windowOpen,
                        maxResults,
                        result
                    );
                }
                else
                {
                    return await RenderMarketActivityChurn(
                        normalizedBucket,
                        targetDate,
                        comparisonDate,
                        windowOpen,
                        maxResults,
                        result
                    );
                }
            },
            "GetMarketWide13FActivity",
            $"bucket: {bucket}"
        );
    }

    private async Task<(
        DateOnly Target,
        DateOnly? Previous,
        bool WindowOpen,
        string Note,
        HoldingsCorpusStatus Coverage,
        string Error
    )> ResolveMarketActivityDates(string reportDate)
    {
        // 13F-only: the prior entry must be the prior QUARTER. The all-filings list now
        // carries daily 13D/G event dates, which would make "previous" the prior day and
        // compare a quarter-end portfolio against a single-day stake. Served from the
        // repository's process-wide cache — the live DISTINCT scan measures ~28s against a
        // 30s command timeout, so resolving off it cold timed out every first call.
        var reportDates = await _holdingRepository.Get13FAvailableReportDatesCached();
        if (reportDates.Count == 0)
            return (default, null, false, null, null, "No 13F holdings data available.");

        var (targetDate, note, error) = ResolveReportDateStrict(reportDate, reportDates);
        if (error != null)
            return (default, null, false, null, null, error);

        var targetIndex = IndexOfDate(reportDates, targetDate);
        if (targetIndex >= reportDates.Count - 1)
        {
            var coverageWithoutPrior = _corpusCoverage.Evaluate(targetDate, null);
            return (targetDate, null, false, note, coverageWithoutPrior, null);
        }

        // While the newest quarter's filing window is open its leaderboards must use the
        // combined queries, or every fund that has not filed yet reads as a mass seller.
        var previousDate = reportDates[targetIndex + 1];
        var windowOpen = targetIndex == 0 && CombinedQuarterHelper.IsFilingWindowOpen(targetDate);
        return (
            targetDate,
            previousDate,
            windowOpen,
            note,
            _corpusCoverage.Evaluate(targetDate, previousDate),
            null
        );
    }

    private static int IndexOfDate(IReadOnlyList<DateOnly> dates, DateOnly target)
    {
        for (var i = 0; i < dates.Count; i++)
        {
            if (dates[i] == target)
                return i;
        }
        return -1;
    }

    // Snapshot-first loads for the market-wide surfaces: a closed quarter reads the
    // plain StockQuarterlyActivity snapshot, the open filing window reads the
    // materialised combined lane — either is ~6k pre-aggregated rows instead of the
    // live two-quarter scan (+ correlated NOT-EXISTS probes for churn/combined) that
    // measured ~30s cold. The live aggregation remains only as a fallback for an
    // empty snapshot: a historical gap before the first-boot backfill, or the window
    // just opened and the first combined drain has not landed yet.
    private async Task<List<MarketWideStockActivity>> LoadMarketActivity(
        DateOnly targetDate,
        DateOnly previousDate,
        bool windowOpen
    )
    {
        var cached = await GetMarketAggregateCached(
            "activity",
            targetDate,
            previousDate,
            windowOpen,
            async () =>
            {
                return await _holdingRepository.GetMarketActivitySnapshotBacked(
                    targetDate,
                    previousDate,
                    windowOpen
                );
            }
        );

        // Split restatement mutates the returned rows. Keep the cached source pristine so a
        // second request cannot compound the adjustment applied by the first request.
        return cached.Select(CloneActivity).ToList();
    }

    // Churn twin of LoadMarketActivity — same lanes, same empty-snapshot fallback.
    private async Task<List<MarketWideStockChurn>> LoadMarketChurn(
        DateOnly targetDate,
        DateOnly previousDate,
        bool windowOpen
    )
    {
        var cached = await GetMarketAggregateCached(
            "churn",
            targetDate,
            previousDate,
            windowOpen,
            async () =>
            {
                var snapshot = windowOpen
                    ? (
                        await _holdingRepository
                            .GetStockActivitySnapshotsCombined(targetDate)
                            .AsNoTracking()
                            .ToListAsync()
                    )
                        .Select(s => s.ToChurn())
                        .ToList()
                    : (
                        await _holdingRepository
                            .GetStockActivitySnapshots(targetDate)
                            .AsNoTracking()
                            .ToListAsync()
                    )
                        .Select(s => s.ToChurn())
                        .ToList();
                if (snapshot.Count > 0)
                    return snapshot;
                return await _holdingRepository
                    .GetQuarterlyNewSoldOutPositions(targetDate, previousDate, windowOpen)
                    .ToListAsync();
            }
        );

        return cached.Select(CloneChurn).ToList();
    }

    // Only resolved 13F quarter dates reach this key builder, so the process-wide lock set grows
    // by a bounded handful of keys per calendar quarter rather than by arbitrary request input.
    private static string MarketActivityCacheKey(
        string source,
        DateOnly targetDate,
        DateOnly previousDate,
        bool windowOpen
    ) =>
        $"holdings:{source}:{(windowOpen ? "combined" : "closed")}:"
        + $"{targetDate.ToString("O", CultureInfo.InvariantCulture)}:"
        + previousDate.ToString("O", CultureInfo.InvariantCulture);

    private sealed record VersionedMarketCacheEntry<T>(DateTime Version, T Value);

    private async Task<T> GetMarketAggregateCached<T>(
        string source,
        DateOnly targetDate,
        DateOnly previousDate,
        bool windowOpen,
        Func<Task<T>> factory
    )
    {
        var key = MarketActivityCacheKey(source, targetDate, previousDate, windowOpen);
        var state = await _holdingRepository.GetMarketActivitySnapshotState(
            targetDate,
            previousDate,
            windowOpen
        );
        if (!state.CanCache)
        {
            _memoryCache.Remove(key);
            // A dirty quarter bypasses the process cache, but still reads the bounded snapshot.
            // Falling back to the holdings corpus here would put the 7-30s aggregates back on
            // every request for the drain's one-hour ingest cooldown. Each snapshot loader falls
            // back to live rows only when its materialized table is genuinely empty.
            return await factory();
        }

        var version = state.ComputedAt!.Value;
        if (
            _memoryCache.TryGetValue(key, out VersionedMarketCacheEntry<T> existing)
            && existing.Version == version
        )
            return existing.Value;

        _memoryCache.Remove(key);
        var cached = await _memoryCache.GetOrCreateSafeAsync(
            key,
            MarketActivityCacheDuration,
            async () => new VersionedMarketCacheEntry<T>(version, await factory())
        );
        var latestState = await _holdingRepository.GetMarketActivitySnapshotState(
            targetDate,
            previousDate,
            windowOpen
        );
        if (latestState.CanCache && latestState.ComputedAt == cached.Version)
            return cached.Value;

        // A refresh can land between the first version read and the single-flight factory.
        // Re-read the version after the factory so that newly built stale entries are evicted
        // before this caller can observe them.
        _memoryCache.Remove(key);
        return await factory();
    }

    private static MarketWideStockActivity CloneActivity(MarketWideStockActivity activity) =>
        new()
        {
            CommonStockId = activity.CommonStockId,
            CurrentShares = activity.CurrentShares,
            PreviousShares = activity.PreviousShares,
            CurrentValue = activity.CurrentValue,
            PreviousValue = activity.PreviousValue,
            CurrentFilerCount = activity.CurrentFilerCount,
            PreviousFilerCount = activity.PreviousFilerCount,
            ListingShares = activity.ListingShares.ToList(),
        };

    private static MarketWideStockChurn CloneChurn(MarketWideStockChurn churn) =>
        new()
        {
            CommonStockId = churn.CommonStockId,
            NewFilerCount = churn.NewFilerCount,
            SoldOutFilerCount = churn.SoldOutFilerCount,
        };

    private async Task<string> RenderMarketActivityMovers(
        string normalizedBucket,
        DateOnly targetDate,
        DateOnly previousDate,
        bool windowOpen,
        int maxResults,
        StringBuilder result
    )
    {
        // Materialize the whole quarter's activity, then restate each stock's share counts onto
        // today's split basis BEFORE bucketing and the Δ Shares column. A split between the two
        // report dates sits the quarters on different bases, so a flat position would otherwise
        // read as a phantom buyer/seller. Δ Value is a dollar figure and is split-invariant (it
        // drives the ordering). The restatement is per-stock, so it cannot translate to SQL.
        // While the filing window is open the combined variant carries non-filers forward at a
        // zero delta, so only real reported moves rank.
        var activity = await LoadMarketActivity(targetDate, previousDate, windowOpen);
        await _marketActivityShareRestater.RestateMarketActivity(
            activity,
            targetDate,
            previousDate
        );

        var movers = activity.Where(a => a.CurrentShares != a.PreviousShares);
        var rows =
            normalizedBucket == "top-buys"
                ? movers.TopBuyers().Take(maxResults).ToList()
                : movers.TopSellers().Take(maxResults).ToList();
        if (rows.Count == 0)
            return result + "_No stocks moved in this direction this quarter._";

        var stocks = await LoadStocksByIds(rows.Select(r => r.CommonStockId).ToList());

        result.AppendNumberedTable(
            "| # | Ticker | Company | Δ Shares | Δ Value ($M) |",
            "|---|--------|---------|---------|-------------|",
            rows,
            (rank, r) =>
            {
                var (ticker, name) = ResolveStockCells(stocks, r.CommonStockId);
                return $"| {rank} | {ticker} | {name} | {FormatSignedShares(r.DeltaShares)} | {FormatSignedMillions(r.DeltaValue)} |";
            }
        );
        result.AppendLine();
        result.AppendLine(DeltaValueCaveat);
        return result.ToString();
    }

    // Δ Value is the change in stored quarter-end position value, so it moves with the stock's own price on
    // positions merely held through the quarter. Without this, the "most bought" stock reads as
    // heavy accumulation when the share change was a rounding error and the price simply rose.
    private const string DeltaValueCaveat =
        "_Δ Value is the change in stored quarter-end position value and includes the quarter's price move on "
        + "held positions, not just net buying or selling — read Δ Shares for the position change itself._";

    private async Task<string> RenderMarketActivityChurn(
        string normalizedBucket,
        DateOnly targetDate,
        DateOnly previousDate,
        bool windowOpen,
        int maxResults,
        StringBuilder result
    )
    {
        // Combined while the window is open: the plain variant counts every fund that has not
        // filed yet as "exited", so sold-out-positions would rank by non-filers, not exits.
        var churn = await LoadMarketChurn(targetDate, previousDate, windowOpen);
        var rows = (
            normalizedBucket == "new-positions"
                ? churn.NewPositions().Take(maxResults)
                : churn.SoldOutPositions().Take(maxResults)
        ).ToList();
        if (rows.Count == 0)
            return result + "_No stocks in this bucket this quarter._";

        var stocks = await LoadStocksByIds(rows.Select(r => r.CommonStockId).ToList());

        var label = normalizedBucket == "new-positions" ? "# Filers Initiated" : "# Filers Exited";
        result.AppendNumberedTable(
            $"| # | Ticker | Company | {label} |",
            "|---|--------|---------|-------------|",
            rows,
            (rank, r) =>
            {
                var (ticker, name) = ResolveStockCells(stocks, r.CommonStockId);
                var count =
                    normalizedBucket == "new-positions" ? r.NewFilerCount : r.SoldOutFilerCount;
                // Format with InvariantCulture so the MCP markdown does not fork the
                // separators by host locale (e.g. de-DE would render 1.000).
                var countCell = McpFormat.WholeNumber(count);
                return $"| {rank} | {ticker} | {name} | {countCell} |";
            }
        );
        return result.ToString();
    }

    [McpServerTool(Name = "GetMostHeldStocks", Title = "Most Widely Held Stocks", ReadOnly = true)]
    [Description(
        "Get the cross-sectional ranking of stocks by institutional 13F breadth for a given quarter. Returns the stocks ranked by number of 13F filers reporting them as a holding (default), by quarter-over-quarter change in filer count (warming names — 'filersDelta' — or cooling names — 'filersDeltaAsc'), or by total reported dollar value. Includes Δ filers vs the prior quarter, total value, Δ value, and the stock's share of the 13F universe. The output publishes the first complete 13F report quarter; earlier rankings are unavailable, and boundary-quarter deltas are withheld. Only currently-held stocks rank; fully-sold-out names live in GetMarketWide13FActivity's sold-out-positions bucket. While the newest quarter's filing window is open, funds that have not filed yet are carried at their prior-quarter positions (noted in the output). Use this to answer 'which stocks are most owned by institutions right now, and is breadth expanding or contracting?'"
    )]
    public Task<string> GetMostHeldStocks(
        [Description(
            "Quarter-end 13F report date in YYYY-MM-DD format, e.g. 2026-03-31 (defaults to the latest available 13F quarter; an off-quarter date snaps to the nearest report on or before it)"
        )]
            string reportDate = null,
        [Description(
            "Sort by: 'filers' (default, # of 13F filers desc), 'filersDelta' (QoQ filer-count delta desc — warming names), 'filersDeltaAsc' (QoQ filer-count delta asc — cooling names), or 'value' (current total reported $ value desc)"
        )]
            string sort = "filers",
        [Description("Maximum number of stocks to return (default: 25, clamped to 1-500)")]
            int maxResults = 25
    )
    {
        return _runner.Execute(
            async () =>
            {
                var normalizedSort = (sort ?? "filers").Trim().ToLowerInvariant();
                if (!ValidMostHeldSorts.Contains(normalizedSort))
                    return McpOutput.InvalidArgument(
                        "sort",
                        sort,
                        string.Join(", ", ValidMostHeldSorts)
                    );

                var (targetDate, previousDate, windowOpen, dateNote, coverage, error) =
                    await ResolveMarketActivityDates(reportDate);
                if (error != null)
                    return error;

                if (!coverage.IsWithinCoverage)
                {
                    return $"Most-held 13F ranking for {FormatDate(targetDate)} is unavailable. "
                        + coverage.ComparisonUnavailableReason;
                }

                var deltaSort = normalizedSort is "filersdelta" or "filersdeltaasc";
                if (deltaSort && !coverage.ComparisonAvailable)
                {
                    return $"Sort '{normalizedSort}' is unavailable for {FormatDate(targetDate)}. "
                        + coverage.ComparisonUnavailableReason
                        + " Use 'filers' or 'value' for the current-quarter ranking.";
                }

                var queryPreviousDate =
                    previousDate ?? HoldingsCorpusCoverage.PreviousQuarterEnd(targetDate);

                // Combined while the window is open — the as-filed ranking would order the
                // whole market by which funds happened to file early. Snapshot-first like
                // the movers/churn buckets; GetMostHeld's CurrentFilerCount > 0 filter and
                // the sort run in memory over the ~6k mapped rows.
                var ranking = (
                    await LoadMarketActivity(targetDate, queryPreviousDate, windowOpen)
                ).Where(a => a.CurrentFilerCount > 0);
                ranking = normalizedSort switch
                {
                    "filersdelta" => ranking
                        .OrderByDescending(a => a.CurrentFilerCount - a.PreviousFilerCount)
                        .ThenByDescending(a => a.CurrentFilerCount)
                        .ThenByDescending(a => a.CurrentValue)
                        .ThenBy(a => a.CommonStockId),
                    "filersdeltaasc" => ranking
                        .OrderBy(a => a.CurrentFilerCount - a.PreviousFilerCount)
                        .ThenByDescending(a => a.CurrentFilerCount)
                        .ThenByDescending(a => a.CurrentValue)
                        .ThenBy(a => a.CommonStockId),
                    "value" => ranking
                        .OrderByDescending(a => a.CurrentValue)
                        .ThenByDescending(a => a.CurrentFilerCount)
                        .ThenBy(a => a.CommonStockId),
                    _ => ranking
                        .OrderByDescending(a => a.CurrentFilerCount)
                        .ThenByDescending(a => a.CurrentValue)
                        .ThenBy(a => a.CommonStockId),
                };
                var rows = ranking.Take(McpLimit.Clamp(maxResults)).ToList();
                if (rows.Count == 0)
                    return $"No stocks were held by 13F filers as of {FormatDate(targetDate)}.";

                var universeFilers = await GetMarketAggregateCached(
                    "universe-filers",
                    targetDate,
                    queryPreviousDate,
                    windowOpen,
                    () =>
                        _holdingRepository.Get13FUniverseFilerCount(
                            targetDate,
                            queryPreviousDate,
                            windowOpen
                        )
                );
                var stocks = await LoadStocksByIds(rows.Select(r => r.CommonStockId).ToList());

                var table = RenderMostHeldStocksTable(
                    targetDate,
                    previousDate,
                    normalizedSort,
                    universeFilers,
                    coverage.ComparisonAvailable,
                    rows,
                    stocks
                );
                var trailingNotes = JoinNotes(
                    dateNote,
                    $"Coverage: complete 13F data begins {FormatDate(coverage.CoverageStartDate)}.",
                    coverage.ComparisonAvailable
                        ? null
                        : $"Note: {coverage.ComparisonUnavailableReason} Delta columns are shown as —.",
                    windowOpen
                        ? $"Note: the {FormatDate(targetDate)} filing window is still open — "
                            + "funds that have not filed yet carry their prior-quarter positions."
                        : null
                );
                return trailingNotes == null ? table : $"{table}\n{trailingNotes}";
            },
            "GetMostHeldStocks",
            $"sort: {sort}, max: {maxResults}"
        );
    }

    private static string RenderMostHeldStocksTable(
        DateOnly targetDate,
        DateOnly? previousDate,
        string sort,
        int universeFilers,
        bool comparisonAvailable,
        List<MarketWideStockActivity> rows,
        IDictionary<Guid, CommonStock> stocks
    )
    {
        var result = new StringBuilder();
        result.AppendLine($"Most-held 13F stocks as of {FormatDate(targetDate)}");
        var comparisonNote =
            comparisonAvailable && previousDate.HasValue
                ? PriorQuarterSubtitle(previousDate.Value)
                : "No prior report quarter is available within complete coverage";
        result.AppendLine(
            $"{comparisonNote} · {McpFormat.WholeNumber(universeFilers)} filers in the 13F universe"
        );
        result.AppendLine($"Sorted by: {sort}");
        result.AppendLine();
        result.AppendNumberedTable(
            "| # | Ticker | Company | # Filers | Δ Filers (QoQ) | Total $ Value ($M) | Δ $ Value ($M) | % of 13F Universe |",
            "|---|--------|---------|----------|----------------|--------------------|----------------|-------------------|",
            rows,
            (rank, r) =>
            {
                var (ticker, name) = ResolveStockCells(stocks, r.CommonStockId);
                var pct = Percentage.Of(r.CurrentFilerCount, universeFilers);
                var deltaFilers = comparisonAvailable
                    ? FormatSignedShares(r.CurrentFilerCount - r.PreviousFilerCount)
                    : "—";
                var deltaValue = comparisonAvailable ? FormatSignedMillions(r.DeltaValue) : "—";
                return $"| {rank} | {ticker} | {name} | {McpFormat.WholeNumber(r.CurrentFilerCount)} | {deltaFilers} | {FormatMillions(r.CurrentValue)} | {deltaValue} | {FormatPercent(pct)}% |";
            }
        );
        return result.ToString();
    }

    [McpServerTool(
        Name = "GetInstitutionSummary",
        Title = "Institution Portfolio Summary",
        ReadOnly = true
    )]
    [Description(
        "Get the portfolio summary header for an institutional 13F filer — 13F reported value (long U.S. positions only, not total firm AUM), position count, top-10 / top-25 concentration, QoQ turnover, and the latest / prior report dates with the count of quarters tracked in this database. Resolve exact CIKs with SearchInstitutions; ambiguous partial names return candidates rather than selecting a filer silently."
    )]
    public Task<string> GetInstitutionSummary(
        [Description(
            "Institution name or CIK (a unique partial resolves; ambiguous partials return candidate CIKs)"
        )]
            string institutionName,
        [Description(
            "Quarter-end 13F report date in YYYY-MM-DD format (defaults to the holder's latest; an off-quarter date snaps to the nearest report on or before it)"
        )]
            string reportDate = null
    )
    {
        return _runner.Execute(
            async () =>
            {
                var (holder, reportDates, targetDate, notes, error) =
                    await ResolveHolderAndTargetDate(institutionName, reportDate);
                if (error != null)
                    return error;

                var previousDate = GetPriorReportDate(reportDates, targetDate);
                var summary = await _summaryProvider.Get(
                    holder,
                    targetDate,
                    previousDate,
                    reportDates.Count
                );

                return RenderInstitutionSummary(holder, targetDate, previousDate, summary, notes);
            },
            "GetInstitutionSummary",
            $"institution: {institutionName}"
        );
    }

    private static string RenderInstitutionSummary(
        InstitutionalHolder holder,
        DateOnly targetDate,
        DateOnly? previousDate,
        InstitutionPortfolioSummary summary,
        string notes = null
    )
    {
        var result = new StringBuilder();
        result.AppendLine($"Portfolio summary — **{holder.Name}** as of {FormatDate(targetDate)}");
        if (previousDate.HasValue)
            result.AppendLine(PriorQuarterSubtitle(previousDate.Value));
        if (notes != null)
            result.AppendLine(notes);
        result.AppendLine();
        result.AppendLine("| Metric | Value |");
        result.AppendLine("|--------|-------|");
        // The CIK is how a caller chains this fund into every other institution route, and
        // a name-resolved lookup has no other way to learn it.
        result.AppendLine($"| CIK | {holder.Cik} |");
        result.AppendLine($"| Reported AUM | ${McpFormat.WholeNumber(summary.ReportedAum)} |");
        result.AppendLine($"| # Positions | {McpFormat.WholeNumber(summary.PositionCount)} |");
        result.AppendLine(
            $"| Top 10 concentration | {FormatPercent(summary.Top10ConcentrationPercent)}% |"
        );
        result.AppendLine(
            $"| Top 25 concentration | {FormatPercent(summary.Top25ConcentrationPercent)}% |"
        );
        result.AppendLine($"| QoQ turnover | {FormatPercent(summary.QoQTurnoverPercent)}% |");
        result.AppendLine($"| Quarters tracked | {summary.QuartersReported} |");
        result.AppendLine();
        result.AppendLine(
            "_Reported AUM = total value of 13F-reportable U.S. positions — it excludes cash, bonds, non-U.S. holdings, and short stock, and is NOT the firm's total assets under management. It DOES include the notional value of reported put and call positions, so a filer expressing its views in options can show an AUM far larger than the equity it actually holds._"
        );
        result.AppendLine(
            "_Quarters tracked counts the 13F quarters in this database, not the filer's full filing history._"
        );
        result.AppendLine(
            "_QoQ turnover = (Σ |Δ shares × current price proxy|) / (2 × AUM), where the per-share price proxy is the current quarter's Value / Shares._"
        );

        if (holder.ConfidentialTreatmentRequested)
        {
            result.AppendLine();
            result.AppendLine(
                "⚠️ **Confidential Treatment** — This manager has requested confidential treatment for one or more investments in the most recent 13F filing. The portfolio shown may be incomplete."
            );
        }

        return result.ToString();
    }

    private static string RenderSectorAllocationTable(
        InstitutionalHolder holder,
        DateOnly targetDate,
        List<IndustryAllocationSlice> slices,
        string groupLabel = "Industry",
        string notes = null
    )
    {
        var result = new StringBuilder();
        result.AppendLine($"Sector allocation — **{holder.Name}** as of {FormatDate(targetDate)}");
        if (notes != null)
            result.AppendLine(notes);
        result.AppendLine();
        if (slices.Count == 0)
        {
            result.AppendLine("_No holdings reported for the selected quarter._");
            return result.ToString();
        }

        result.AppendNumberedTable(
            $"| # | {groupLabel} | # Positions | Value ($M) | % of Portfolio |",
            "|---|----------|-------------|------------|----------------|",
            slices,
            (rank, s) =>
                $"| {rank} | {s.IndustryName} | {McpFormat.WholeNumber(s.PositionCount)} | {FormatMillions(s.TotalValue)} | {FormatPercent(s.PercentOfPortfolio)}% |"
        );

        result.AppendLine();
        result.AppendLine(
            $"Total: {McpFormat.WholeNumber(slices.Sum(s => s.PositionCount))} positions, "
                + $"${FormatMillions(slices.Sum(s => s.TotalValue))}M 13F value. "
                + "_Percentages are of the 13F-reported (long U.S. equity) book only._"
        );

        if (holder.ConfidentialTreatmentRequested)
        {
            result.AppendLine();
            result.AppendLine(
                "⚠️ **Confidential Treatment** — This manager has requested confidential treatment for one or more investments in the most recent 13F filing. The allocation shown may be incomplete."
            );
        }

        return result.ToString();
    }

    [McpServerTool(
        Name = "GetInstitutionSectorAllocation",
        Title = "Institution Sector Allocation",
        ReadOnly = true
    )]
    [Description(
        "Get an institution's 13F portfolio allocation for a given report quarter (defaults to the latest), grouped by fine-grained industry (default) or rolled up by sector via `groupBy`. Returns a markdown table sorted by % of portfolio descending, with stocks lacking a classification collapsed into a single 'Unclassified' row at the end. Use SearchInstitutions for an exact CIK; ambiguous partial names return candidates instead of selecting silently."
    )]
    public Task<string> GetInstitutionSectorAllocation(
        [Description(
            "Institution name or CIK (a unique partial resolves; ambiguous partials return candidate CIKs)"
        )]
            string institutionName,
        [Description(
            "Quarter-end 13F report date in YYYY-MM-DD format (defaults to the holder's latest; an off-quarter date snaps to the nearest report on or before it)"
        )]
            string reportDate = null,
        [Description(
            "Grouping level: 'industry' (default, fine-grained) or 'sector' (broad rollup)"
        )]
            string groupBy = "industry"
    )
    {
        return _runner.Execute(
            async () =>
            {
                var normalizedGroupBy = (groupBy ?? "industry").Trim().ToLowerInvariant();
                if (normalizedGroupBy is not ("industry" or "sector"))
                    return McpOutput.InvalidArgument("groupBy", groupBy, "industry, sector");

                var (holder, _, targetDate, notes, error) = await ResolveHolderAndTargetDate(
                    institutionName,
                    reportDate
                );
                if (error != null)
                    return error;

                var holdings = await _holdingRepository
                    .Get13FByHolder(holder, targetDate)
                    .Include(h => h.CommonStock)
                        .ThenInclude(s => s.Industry)
                            .ThenInclude(i => i.Sector)
                    .ToListAsync();
                var slices =
                    normalizedGroupBy == "sector"
                        ? IndustryAllocationCalculator.CalculateBySector(holdings)
                        : IndustryAllocationCalculator.Calculate(holdings);

                return RenderSectorAllocationTable(
                    holder,
                    targetDate,
                    slices,
                    normalizedGroupBy == "sector" ? "Sector" : "Industry",
                    notes
                );
            },
            "GetInstitutionSectorAllocation",
            $"institution: {institutionName}"
        );
    }

    [McpServerTool(
        Name = "GetInstitutionQuarterlyActivity",
        Title = "Institution Quarterly Activity",
        ReadOnly = true
    )]
    [Description(
        "Get an institution's quarterly position-change activity — Initiated / Increased / Reduced / Exited stocks diffed against the immediately prior quarter. Returns the buckets as one markdown section per bucket, sorted by absolute Δ market-value desc (Δ Value includes price movement, not just trading). Use `bucket` to filter to a single bucket. Use this to answer 'what did this fund do this quarter?'"
    )]
    public Task<string> GetInstitutionQuarterlyActivity(
        [Description(
            "Institution name or CIK (a unique partial resolves; ambiguous partials return candidate CIKs)"
        )]
            string institutionName,
        [Description(
            "Quarter-end 13F report date in YYYY-MM-DD format (defaults to the holder's latest; an off-quarter date snaps to the nearest report on or before it)"
        )]
            string reportDate = null,
        [Description(
            "Filter to a single bucket: initiated, increased, reduced, exited (omit for all four)"
        )]
            string bucket = null,
        [Description(
            "Maximum number of stocks to return per bucket (default: 20, clamped to 1-500)"
        )]
            int maxResults = 20
    )
    {
        return _runner.Execute(
            async () =>
            {
                var normalizedBucket = bucket?.Trim().ToLowerInvariant();
                if (
                    !string.IsNullOrEmpty(normalizedBucket)
                    && !ValidInstitutionActivityBuckets.Contains(normalizedBucket)
                )
                    return "Unknown bucket. Use one of: initiated, increased, reduced, exited (or omit).";

                maxResults = McpLimit.Clamp(maxResults);

                var (holder, matchNote, holderError) = await ResolveHolderByName(institutionName);
                if (holderError != null)
                    return holderError;

                var reportDates = await _holdingRepository.Get13FReportDatesByHolderSnapshotBacked(
                    holder
                );
                if (reportDates.Count < 2)
                    return $"{holder.Name} has fewer than two reported quarters — no diff available.";

                var (targetDate, dateNote, dateError) = ResolveReportDateStrict(
                    reportDate,
                    reportDates
                );
                if (dateError != null)
                    return dateError;
                var priorDate = GetPriorReportDate(reportDates, targetDate);
                if (priorDate == null)
                    return $"{FormatDate(targetDate)} is the oldest reported quarter for {holder.Name} — no prior to compare against.";

                var currentHoldings = await LoadHoldingsByHolderWithStock(holder, targetDate);
                var previousHoldings = await LoadHoldingsByHolderWithStock(holder, priorDate.Value);
                var grouped = HolderQuarterlyActivityCalculator.Group(
                    currentHoldings,
                    previousHoldings
                );

                // Restate every diffed position onto today's split basis before the buckets are
                // read. When a split falls between the two quarters they sit on different bases,
                // so a flat holding would otherwise land in Increased/Reduced as a phantom move.
                // Initiated/Exited are defined by a zero side that restatement preserves; only
                // the Increased/Reduced/Unchanged split can flip, so re-bucket those. Δ Value is
                // split-invariant and drives the ordering — left untouched.
                await RestateAndRebucketQuarterlyActivity(grouped, targetDate, priorDate.Value);

                return RenderQuarterlyActivity(
                    holder,
                    targetDate,
                    priorDate.Value,
                    grouped,
                    normalizedBucket,
                    maxResults,
                    JoinNotes(matchNote, dateNote)
                );
            },
            "GetInstitutionQuarterlyActivity",
            $"institution: {institutionName}"
        );
    }

    private static string RenderQuarterlyActivity(
        InstitutionalHolder holder,
        DateOnly targetDate,
        DateOnly priorDate,
        Dictionary<StockPositionChangeType, List<StockPositionChange>> grouped,
        string normalizedBucket,
        int maxResults,
        string notes = null
    )
    {
        var sections = new (StockPositionChangeType Type, string Label)[]
        {
            (StockPositionChangeType.Initiated, "Initiated"),
            (StockPositionChangeType.Increased, "Increased"),
            (StockPositionChangeType.Reduced, "Reduced"),
            (StockPositionChangeType.Exited, "Exited"),
        };

        var result = new StringBuilder();
        result.AppendLine($"Quarterly activity — **{holder.Name}** as of {FormatDate(targetDate)}");
        result.AppendLine(PriorQuarterSubtitle(priorDate));
        if (notes != null)
            result.AppendLine(notes);
        result.AppendLine();

        var rendered = 0;
        var selectedSections = sections.Where(s =>
            string.IsNullOrEmpty(normalizedBucket) || s.Label.ToLowerInvariant() == normalizedBucket
        );
        foreach (var section in selectedSections)
        {
            var bucketRows = grouped[section.Type];
            var rows = bucketRows
                .OrderByDescending(r => Math.Abs(r.DeltaValue))
                .Take(maxResults)
                .ToList();
            if (AppendActivitySection(result, section.Label, rows, bucketRows.Count))
                rendered++;
        }

        if (rendered == 0)
        {
            result.AppendLine("_No matching buckets._");
            return result.ToString();
        }

        result.AppendLine(
            "_Δ Value is the change in stored quarter-end position value and includes price movement, not just trading — it also drives the per-bucket ordering._"
        );
        return result.ToString();
    }

    private static bool AppendActivitySection(
        StringBuilder result,
        string label,
        List<StockPositionChange> rows,
        int bucketTotal
    )
    {
        // The heading carries the bucket's real size when maxResults trims it, so a capped
        // list is never mistaken for "the fund initiated exactly N positions".
        result.AppendLine(
            rows.Count < bucketTotal
                ? $"## {label} (top {rows.Count} of {bucketTotal} by |Δ value|)"
                : $"## {label}"
        );
        if (rows.Count == 0)
        {
            result.AppendLine("_No stocks in this bucket this quarter._");
            result.AppendLine();
            return false;
        }
        result.AppendNumberedTable(
            "| # | Ticker | Company | Prior | New | Δ Shares | Δ Value ($M) |",
            "|---|--------|---------|-------|-----|---------|-------------|",
            rows,
            (rank, r) =>
                $"| {rank} | {r.Ticker} | {r.Name} | {McpFormat.WholeNumber(r.PreviousShares)} | {McpFormat.WholeNumber(r.CurrentShares)} | {FormatSignedShares(r.DeltaShares)} | {FormatSignedMillions(r.DeltaValue)} |"
        );
        result.AppendLine();
        return true;
    }

    [McpServerTool(
        Name = "GetFundOverlap",
        Title = "Portfolio Overlap Between Institutions",
        ReadOnly = true
    )]
    [Description(
        "Get the 13F portfolio overlap between two institutions for their latest common report date — Jaccard similarity, dollar-weighted overlap ($-weighted = shared dollars, taking the smaller of the two funds' values per stock, as a share of union dollars), per-fund position counts and totals, and a side-by-side table of stocks with per-fund shares + percent of portfolio. Covers 13F institutional managers only — find names with SearchInstitutions; for mutual-fund/ETF (NPORT) portfolios use GetFundHoldings. Use this to answer 'do these two funds own the same stocks?' or 'where do their portfolios diverge?'"
    )]
    public Task<string> GetFundOverlap(
        [Description(
            "First institution name or CIK (a unique partial resolves; ambiguous partials return candidate CIKs)"
        )]
            string institutionName1,
        [Description(
            "Second institution name or CIK (a unique partial resolves; ambiguous partials return candidate CIKs)"
        )]
            string institutionName2,
        [Description(
            "Quarter-end 13F report date in YYYY-MM-DD format (defaults to the latest common quarter; an off-quarter date snaps to the nearest common report on or before it)"
        )]
            string reportDate = null,
        [Description("Maximum number of stocks to return (default: 30, clamped to 1-500)")]
            int maxResults = 30
    )
    {
        return _runner.Execute(
            async () =>
            {
                maxResults = McpLimit.Clamp(maxResults);

                var (holder1, matchNote1, holder1Error) = await ResolveHolderByName(
                    institutionName1
                );
                if (holder1Error != null)
                    return holder1Error;
                var (holder2, matchNote2, holder2Error) = await ResolveHolderByName(
                    institutionName2
                );
                if (holder2Error != null)
                    return holder2Error;

                var (selected, dateNote, error) = await ResolveCommonReportDate(
                    holder1,
                    holder2,
                    reportDate
                );
                if (error != null)
                    return error;

                var holdings1 = await LoadHoldingsByHolderWithStock(holder1, selected);
                var holdings2 = await LoadHoldingsByHolderWithStock(holder2, selected);
                var overlap = FundOverlapCalculator.Calculate(
                    [
                        (holder1, (IReadOnlyList<InstitutionalHolding>)holdings1),
                        (holder2, (IReadOnlyList<InstitutionalHolding>)holdings2),
                    ],
                    selected
                );

                return RenderOverlapTable(
                    holder1,
                    holder2,
                    selected,
                    overlap,
                    maxResults,
                    JoinNotes(matchNote1, matchNote2, dateNote)
                );
            },
            "GetFundOverlap",
            $"funds: {institutionName1}, {institutionName2}"
        );
    }

    private async Task<(DateOnly Selected, string Note, string Error)> ResolveCommonReportDate(
        InstitutionalHolder holder1,
        InstitutionalHolder holder2,
        string reportDate
    )
    {
        var common = await ComputeCommonReportDates([holder1, holder2]);
        if (common.Count == 0)
            return (
                default,
                null,
                $"{holder1.Name} and {holder2.Name} share no common report dates."
            );

        return ResolveReportDateStrict(reportDate, common);
    }

    private async Task<List<DateOnly>> ComputeCommonReportDates(IList<InstitutionalHolder> holders)
    {
        var perHolder = new List<List<DateOnly>>(holders.Count);
        foreach (var holder in holders)
            perHolder.Add(await _holdingRepository.Get13FReportDatesByHolderSnapshotBacked(holder));

        return perHolder
            .Skip(1)
            .Aggregate((IEnumerable<DateOnly>)perHolder[0], (acc, next) => acc.Intersect(next))
            .OrderByDescending(d => d)
            .ToList();
    }

    private static string RenderOverlapTable(
        InstitutionalHolder holder1,
        InstitutionalHolder holder2,
        DateOnly selected,
        FundOverlapResult overlap,
        int maxResults,
        string notes = null
    )
    {
        var title =
            $"Portfolio overlap — **{holder1.Name}** vs **{holder2.Name}** as of {FormatDate(selected)}";
        if (notes != null)
            title = $"{title}\n{notes}";
        var result = MarkdownTable.Start(title, "| Metric | Value |", "|--------|-------|");
        // Per-fund position counts + totals make a gross size mismatch (or a wrong-entity
        // match) legible before the reader interprets a near-zero overlap percentage.
        for (var i = 0; i < overlap.Funds.Count; i++)
        {
            var fund = overlap.Funds[i];
            result.AppendLine(
                $"| {(char)('A' + i)}: {fund.HolderName} | {McpFormat.WholeNumber(fund.PositionCount)} positions, ${FormatMillions(fund.TotalValue)}M |"
            );
        }
        result.AppendLine(
            $"| Union positions | {McpFormat.WholeNumber(overlap.UnionPositionCount)} |"
        );
        result.AppendLine(
            $"| Shared positions | {McpFormat.WholeNumber(overlap.IntersectionPositionCount)} |"
        );
        result.AppendLine(
            $"| Jaccard similarity | {FormatPercent(overlap.JaccardSimilarityPercent)}% |"
        );
        result.AppendLine(
            $"| $-weighted overlap | {FormatPercent(overlap.DollarWeightedOverlapPercent)}% |"
        );
        result.AppendLine();

        if (overlap.Rows.Count == 0)
        {
            result.AppendLine("_Neither fund reports any positions for this date._");
            return result.ToString();
        }

        var rendered = overlap.Rows.Take(maxResults).ToList();
        result.AppendNumberedTable(
            "| # | Ticker | Company | A Shares | A % | B Shares | B % | Combined ($M) |",
            "|---|--------|---------|---------|-----|---------|-----|---------------|",
            rendered,
            (rank, row) =>
            {
                var a = row.Slices[0];
                var b = row.Slices[1];
                return $"| {rank} | {row.Ticker} | {row.Name} | {(a.Shares > 0 ? McpFormat.WholeNumber(a.Shares) : "—")} | {(a.Value > 0 ? FormatPercent(a.PercentOfPortfolio) + "%" : "—")} | {(b.Shares > 0 ? McpFormat.WholeNumber(b.Shares) : "—")} | {(b.Value > 0 ? FormatPercent(b.PercentOfPortfolio) + "%" : "—")} | {FormatMillions(row.CombinedValue)} |";
            }
        );

        result.AppendLine();
        result.AppendLine(
            "_$-weighted overlap = shared dollars (the smaller of the two funds' values per stock) as a share of union dollars (the larger per stock)._"
        );
        var truncation = McpOutput.TruncationNote(rendered.Count, overlap.Rows.Count);
        if (truncation.Length > 0)
            result.AppendLine(truncation);

        return result.ToString();
    }

    [McpServerTool(
        Name = "GetConsensusHoldings",
        Title = "Consensus Holdings Across Institutions",
        ReadOnly = true
    )]
    [Description(
        "Get the consensus / combined portfolio of 2-25 institutions for their latest common report date. Returns stocks ranked by how many of the supplied funds hold them (descending), then by combined value. Filter by `minFunds` to only show stocks held by at least that many funds. Use this to answer 'what do these funds agree on?' or 'show me the top picks across these N investors combined.'"
    )]
    public Task<string> GetConsensusHoldings(
        [Description(
            "Comma- or semicolon-separated institution names or CIKs. Unique partial names and verified brand aliases resolve; ambiguous partials return candidate CIKs. 2-25 names."
        )]
            string institutionNames,
        [Description(
            "Quarter-end 13F report date in YYYY-MM-DD format (defaults to the latest common quarter; an off-quarter date snaps to the nearest common report on or before it)"
        )]
            string reportDate = null,
        [Description(
            "Minimum number of funds a stock must be held by to appear (default: 1 — note that 1 also includes stocks held by a single fund; set 2+ for true consensus)"
        )]
            int minFunds = 1,
        [Description("Maximum number of stocks to return (default: 30, clamped to 1-500)")]
            int maxResults = 30
    )
    {
        return _runner.Execute(
            async () =>
            {
                var names = (institutionNames ?? string.Empty)
                    .Split(
                        [',', ';'],
                        StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
                    )
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                if (names.Count < 2)
                    return "Pass at least two institution names (comma-separated).";
                if (names.Count > 25)
                    return "At most 25 institutions can be combined.";

                var holders = new List<InstitutionalHolder>();
                var missing = new List<string>();
                var ambiguous = new List<string>();
                foreach (var name in names)
                {
                    var resolution = await _holderRepository.ResolveNameOrCik(name);
                    if (resolution.Selected != null)
                        holders.Add(resolution.Selected.Holder);
                    else if (resolution.Candidates.Count == 0)
                        missing.Add(name);
                    else
                        ambiguous.Add(
                            $"'{name}': {FormatResolutionCandidates(resolution.Candidates)}"
                        );
                }

                if (ambiguous.Count > 0)
                    return "Ambiguous institution input — no consensus was calculated. "
                        + string.Join("; ", ambiguous)
                        + ". Pass the intended SEC CIK or an exact filed name.";
                if (holders.Count < 2)
                    return $"Could not resolve enough institutions. Missing: {string.Join(", ", missing)}.";

                // Two inputs can resolve to the same exact filer (for example a brand alias
                // and its CIK). Combining the duplicate would double every combined value.
                holders = holders.DistinctBy(h => h.Id).ToList();
                if (holders.Count < 2)
                    return $"The supplied names all resolve to the same institution — {holders[0].Name} (CIK {holders[0].Cik}). Pass at least two distinct institutions.";

                var common = await ComputeCommonReportDates(holders);
                if (common.Count == 0)
                    return "The selected institutions share no common report dates.";

                var (selected, dateNote, dateError) = ResolveReportDateStrict(reportDate, common);
                if (dateError != null)
                    return dateError;

                maxResults = McpLimit.Clamp(maxResults);

                var perFund =
                    new List<(
                        InstitutionalHolder Holder,
                        IReadOnlyList<InstitutionalHolding> Holdings
                    )>();
                foreach (var holder in holders)
                {
                    var holdings = await LoadHoldingsByHolderWithStock(holder, selected);
                    perFund.Add((holder, holdings));
                }
                var overlap = FundOverlapCalculator.Calculate(perFund, selected);

                var matchingRows = overlap
                    .Rows.Select(r => (Row: r, HeldBy: r.Slices.Count(s => s.Value > 0)))
                    .Where(x => x.HeldBy >= Math.Max(1, minFunds))
                    .OrderByDescending(x => x.HeldBy)
                    .ThenByDescending(x => x.Row.CombinedValue)
                    .ToList();
                var rowsWithConsensus = matchingRows.Take(maxResults).ToList();

                return RenderConsensusHoldingsTable(
                    holders,
                    missing,
                    selected,
                    rowsWithConsensus,
                    matchingRows.Count,
                    dateNote
                );
            },
            "GetConsensusHoldings",
            $"names: {institutionNames}"
        );
    }

    private static string RenderConsensusHoldingsTable(
        List<InstitutionalHolder> holders,
        List<string> missing,
        DateOnly selected,
        List<(FundOverlapRow Row, int HeldBy)> rowsWithConsensus,
        int totalMatchingRows = -1,
        string notes = null
    )
    {
        var result = new StringBuilder();
        result.AppendLine(
            $"Consensus holdings — **{holders.Count} funds** as of {FormatDate(selected)}"
        );
        if (missing.Count > 0)
            result.AppendLine($"_Could not resolve: {string.Join(", ", missing)}._");
        if (notes != null)
            result.AppendLine(notes);
        result.AppendLine();
        result.AppendLine("Funds:");
        foreach (var h in holders)
            result.AppendLine($"- {h.Name} (CIK {h.Cik})");
        result.AppendLine();

        if (rowsWithConsensus.Count == 0)
            return result + "_No stocks meet the minFunds threshold._";

        result.AppendNumberedTable(
            "| # | Ticker | Company | # Funds | Combined ($M) |",
            "|---|--------|---------|---------|---------------|",
            rowsWithConsensus,
            (rank, x) =>
                $"| {rank} | {x.Row.Ticker} | {x.Row.Name} | {x.HeldBy}/{holders.Count} | {McpFormat.Invariant(x.Row.CombinedValue / 1_000_000m, "N1")} |"
        );

        var truncation = McpOutput.TruncationNote(
            rowsWithConsensus.Count,
            totalMatchingRows < 0 ? rowsWithConsensus.Count : totalMatchingRows
        );
        if (truncation.Length > 0)
        {
            result.AppendLine();
            result.AppendLine(truncation);
        }
        return result.ToString();
    }

    private async Task<(
        InstitutionalHolder Holder,
        string MatchNote,
        string Error
    )> ResolveHolderByName(string name)
    {
        var resolution = await _holderRepository.ResolveNameOrCik(name);
        if (resolution.Selected != null)
            return (resolution.Selected.Holder, null, null);
        if (resolution.Candidates.Count == 0)
            return (
                null,
                null,
                $"No match for '{name}' in the tracked 13F filer set. Use SearchInstitutions to inspect the tracked set."
            );

        return (
            null,
            null,
            $"'{name}' is ambiguous in the tracked 13F filer set: {FormatResolutionCandidates(resolution.Candidates)}. Pass the intended SEC CIK or an exact filed name."
        );
    }

    private static string FormatResolutionCandidates(
        IReadOnlyList<InstitutionalHolderSearchMatch> candidates
    ) =>
        string.Join(
            "; ",
            candidates.Select(c =>
                $"{MarkdownTable.EscapeCell(c.Holder.Name, "—")} (CIK {c.Holder.Cik}, latest {FormatOptionalDate(c.LatestReportDate)}, reported AUM {FormatOptionalDollars(c.ReportedAum)}, positions {FormatOptionalCount(c.PositionCount)})"
            )
        );

    // 13F-only: a Schedule 13D/G stake whose event date coincides with a 13F quarter end
    // shares the holdings table and would double-count the position in every per-holder
    // portfolio composition built on this load (consensus, overlap, quarterly activity) —
    // the holder-side twin of GH-4449.
    private Task<List<InstitutionalHolding>> LoadHoldingsByHolderWithStock(
        InstitutionalHolder holder,
        DateOnly reportDate
    ) => _holdingRepository.Get13FByHolderWithStock(holder, reportDate).ToListAsync();

    private Task<Dictionary<Guid, CommonStock>> LoadStocksByIds(List<Guid> stockIds) =>
        _commonStockRepository.GetByIds(stockIds).ToDictionaryAsync(s => s.Id);

    // Batch-loads the splits for a set of stocks once, grouped by stock, so cross-sectional
    // tools can restate each row's share counts onto today's basis without an N+1 query.
    private async Task<Dictionary<Guid, List<StockSplit>>> LoadSplitsByStock(
        IEnumerable<Guid> stockIds
    )
    {
        var ids = stockIds.Distinct().ToList();
        if (ids.Count == 0)
            return new Dictionary<Guid, List<StockSplit>>();

        var splits = await _stockSplitRepository
            .GetAll()
            .Where(s => ids.Contains(s.CommonStockId))
            .ToListAsync();
        return splits.GroupBy(s => s.CommonStockId).ToDictionary(g => g.Key, g => g.ToList());
    }

    // A stock with no splits restates by factor 1 (no-op), so an absent key returns an empty set.
    private static IReadOnlyList<StockSplit> SplitsFor(
        IReadOnlyDictionary<Guid, List<StockSplit>> splitsByStock,
        Guid stockId
    ) => splitsByStock.TryGetValue(stockId, out var splits) ? splits : [];

    // Restates the quarterly-activity diff onto today's split basis, then re-classifies the
    // movement buckets from the restated counts. A zero side is preserved by restatement, so
    // Initiated/Exited stay put; only Increased/Reduced/Unchanged can flip.
    private async Task RestateAndRebucketQuarterlyActivity(
        Dictionary<StockPositionChangeType, List<StockPositionChange>> grouped,
        DateOnly currentDate,
        DateOnly previousDate
    )
    {
        var splitsByStock = await LoadSplitsByStock(
            grouped.Values.SelectMany(rows => rows).Select(r => r.CommonStockId)
        );

        foreach (var rows in grouped.Values)
        {
            foreach (var r in rows)
            {
                var splits = SplitsFor(splitsByStock, r.CommonStockId);
                r.CurrentShares = SplitAdjustment.AdjustShareCount(
                    r.CurrentShares,
                    currentDate,
                    splits
                );
                r.PreviousShares = SplitAdjustment.AdjustShareCount(
                    r.PreviousShares,
                    previousDate,
                    splits
                );
            }
        }

        var movement = new List<StockPositionChange>();
        movement.AddRange(grouped[StockPositionChangeType.Increased]);
        movement.AddRange(grouped[StockPositionChangeType.Reduced]);
        movement.AddRange(grouped[StockPositionChangeType.Unchanged]);
        grouped[StockPositionChangeType.Increased] = [];
        grouped[StockPositionChangeType.Reduced] = [];
        grouped[StockPositionChangeType.Unchanged] = [];

        foreach (var r in movement)
        {
            var type =
                r.CurrentShares == r.PreviousShares ? StockPositionChangeType.Unchanged
                : r.CurrentShares > r.PreviousShares ? StockPositionChangeType.Increased
                : StockPositionChangeType.Reduced;
            r.ChangeType = type;
            grouped[type].Add(r);
        }
    }

    private static (string Ticker, string Name) ResolveStockCells(
        IDictionary<Guid, CommonStock> stocks,
        Guid stockId
    )
    {
        stocks.TryGetValue(stockId, out var s);
        return (s?.Ticker ?? "—", s?.Name ?? "Unknown");
    }

    // Raw dollar values rendered in $millions with an explicit leading +/- sign.
    // `+` for positive deltas; N0 already emits `-` for negatives.
    private static string FormatSignedShares<T>(T value)
        where T : INumber<T> => (value > T.Zero ? "+" : "") + McpFormat.WholeNumber(value);

    // Signed $millions with one decimal place, invariant culture (matches FormatMillions
    // and the rest of this file; MCP markdown must not fork the separators by host locale).
    private static string FormatSignedMillions(decimal value) =>
        McpFormat.Invariant(value / 1_000_000m, "+#,##0.0;-#,##0.0;0.0");

    // Raw dollar values rendered in $millions with one decimal place, invariant culture.
    private static string FormatMillions(decimal value) =>
        McpFormat.Invariant(value / 1_000_000m, "N1");

    // Percentages rendered with one decimal place in invariant culture; callers append the
    // literal `%`. Keeps the separator stable across host locales like the other helpers.
    private static string FormatPercent<T>(T value)
        where T : INumber<T> => McpFormat.Invariant(value, "F1");

    // yyyy-MM-dd dates rendered in invariant culture so the MCP markdown stays Gregorian ISO
    // regardless of the host calendar/locale (LLMs consume these dates as ISO).
    private static string FormatDate(DateOnly date) => McpFormat.Invariant(date, "yyyy-MM-dd");

    // The "vs prior quarter <date>" comparison subtitle is rendered identically across the
    // quarter-over-quarter tables; centralise the wording so the headers stay in sync.
    private static string PriorQuarterSubtitle(DateOnly previousDate) =>
        $"vs prior quarter {FormatDate(previousDate)}";

    private async Task<(
        InstitutionalHolder Holder,
        List<DateOnly> ReportDates,
        DateOnly TargetDate,
        string Notes,
        string Error
    )> ResolveHolderAndTargetDate(string institutionName, string reportDate)
    {
        var (holder, matchNote, holderError) = await ResolveHolderByName(institutionName);
        if (holderError != null)
            return (null, null, default, null, holderError);

        var reportDates = await _holdingRepository.Get13FReportDatesByHolderSnapshotBacked(holder);
        if (reportDates.Count == 0)
            return (holder, null, default, null, $"No 13F holdings reported by {holder.Name}.");

        var (targetDate, dateNote, dateError) = ResolveReportDateStrict(reportDate, reportDates);
        if (dateError != null)
            return (holder, reportDates, default, null, dateError);

        return (holder, reportDates, targetDate, JoinNotes(matchNote, dateNote), null);
    }

    private static bool TryParseReportDate(string input, out DateOnly result)
    {
        result = default;
        return !string.IsNullOrEmpty(input)
            && DateOnly.TryParse(
                input,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out result
            );
    }

    // Strict report-date resolution shared by every reportDate-taking holdings tool. The old
    // helper fell back to validDates[0] (the latest quarter) for ANY bad input, so an LLM
    // asking for a historical quarter could receive current data and present it as historical
    // with no tell beyond the as-of header. Contract (validDates is newest-first):
    // - null/blank         → the latest date, no note (the documented default);
    // - exact match        → that date;
    // - parseable off-list → the nearest report date at or before it (standard as-of
    //                        semantics), with a Note stating the substitution;
    // - unparseable, or a date older than the tracked history → a one-line Error listing the
    //                        available dates. Never a silent fallback.
    private static (DateOnly Date, string Note, string Error) ResolveReportDateStrict(
        string input,
        IReadOnlyList<DateOnly> validDates
    )
    {
        if (string.IsNullOrWhiteSpace(input))
            return (validDates[0], null, null);

        if (!TryParseReportDate(input, out var parsed))
            return (
                default,
                null,
                $"Could not parse reportDate '{input}'. Use YYYY-MM-DD; available report dates: {FormatAvailableDates(validDates)}."
            );

        if (validDates.Contains(parsed))
            return (parsed, null, null);

        foreach (var candidate in validDates)
        {
            if (candidate <= parsed)
                return (
                    candidate,
                    $"Note: {FormatDate(parsed)} is not a 13F report date — showing the nearest report on or before it, {FormatDate(candidate)}. Available: {FormatAvailableDates(validDates)}.",
                    null
                );
        }

        return (
            default,
            null,
            $"No 13F report on or before {FormatDate(parsed)}. Available report dates: {FormatAvailableDates(validDates)}."
        );
    }

    private static string FormatAvailableDates(IReadOnlyList<DateOnly> validDates) =>
        string.Join(", ", validDates.Take(5).Select(FormatDate))
        + (validDates.Count > 5 ? ", …" : "");

    // Report-date lists are newest-first, so the prior quarter sits at the next index.
    // Returns null when the target is absent from the list or is already the oldest quarter.
    private static DateOnly? GetPriorReportDate(List<DateOnly> reportDates, DateOnly targetDate)
    {
        var index = reportDates.IndexOf(targetDate);
        if (index < 0 || index >= reportDates.Count - 1)
            return null;
        return reportDates[index + 1];
    }

    // Thin forwarder so existing reflection-based normalization tests still find the method.
    private Task<(CommonStock Stock, string Error)> ResolveStockByTicker(string ticker) =>
        _commonStockRepository.ResolveByTicker(ticker);
}
