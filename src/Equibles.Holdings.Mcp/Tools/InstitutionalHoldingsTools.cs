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
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Repositories;
using Equibles.Holdings.Repositories.Extensions;
using Equibles.Holdings.Repositories.Models;
using Equibles.Mcp;
using Equibles.Mcp.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace Equibles.Holdings.Mcp.Tools;

[McpServerToolType]
public class InstitutionalHoldingsTools
{
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

    private static readonly string[] ValidMostHeldSorts = ["filers", "filersdelta", "value"];

    private readonly InstitutionalHoldingRepository _holdingRepository;
    private readonly InstitutionalHolderRepository _holderRepository;
    private readonly CommonStockRepository _commonStockRepository;
    private readonly StockSplitRepository _stockSplitRepository;
    private readonly McpToolRunner _runner;

    public InstitutionalHoldingsTools(
        InstitutionalHoldingRepository holdingRepository,
        InstitutionalHolderRepository holderRepository,
        CommonStockRepository commonStockRepository,
        StockSplitRepository stockSplitRepository,
        ErrorManager errorManager,
        ILogger<InstitutionalHoldingsTools> logger
    )
    {
        _holdingRepository = holdingRepository;
        _holderRepository = holderRepository;
        _commonStockRepository = commonStockRepository;
        _stockSplitRepository = stockSplitRepository;
        _runner = new McpToolRunner(logger, errorManager.AsMcpErrorReporter());
    }

    [McpServerTool(Name = "GetTopHolders")]
    [Description(
        "Get the top institutional holders (fund managers) of a stock from SEC 13F-HR filings. Returns a ranked list of institutions by shares held, including market value and percentage of total institutional ownership. Data is sourced from quarterly 13F filings that large investment managers are required to file with the SEC. Use this to understand who the major institutional investors in a company are."
    )]
    public Task<string> GetTopHolders(
        [Description("Company ticker symbol (e.g., AAPL, MSFT)")] string ticker,
        [Description("Report date in YYYY-MM-DD format (defaults to latest available)")]
            string reportDate = null,
        [Description("Maximum number of holders to return (default: 20)")] int maxResults = 20
    )
    {
        return _runner.Execute(
            async () =>
            {
                var (stock, stockError) = await _commonStockRepository.ResolveByTicker(ticker);
                if (stockError != null)
                    return stockError;

                var (targetDate, found) = await TryResolveLatestReportDate(
                    reportDate,
                    _holdingRepository.Get13FReportDatesByStock(stock)
                );
                if (!found)
                    return $"No institutional holdings data available for {ticker}.";

                var allHoldings = _holdingRepository.Get13FByStock(stock, targetDate);
                var totalInstitutions = await allHoldings
                    .Select(h => h.InstitutionalHolderId)
                    .Distinct()
                    .CountAsync();
                var totalSharesAll = await allHoldings.SumAsync(h => h.Shares);
                var totalValueAll = await allHoldings.SumAsync(h => h.Value);

                maxResults = McpLimit.Clamp(maxResults);

                var holdings = await allHoldings
                    .OrderByDescending(h => h.Shares)
                    .Take(maxResults)
                    .ToListAsync();

                if (holdings.Count == 0)
                    return $"No institutional holdings found for {ticker} as of {FormatDate(targetDate)}.";

                // All rows share the target report date, so a single factor restates every
                // share count onto today's basis (matching the web). The % of Total is a
                // same-date ratio and is split-invariant (the factor cancels top and bottom).
                var splits = await _stockSplitRepository.GetByStock(stock.Id).ToListAsync();
                var shareFactor = SplitAdjustment.ShareCountFactor(targetDate, splits);

                return RenderTopHoldersTable(
                    stock,
                    ticker,
                    targetDate,
                    totalInstitutions,
                    totalSharesAll,
                    totalValueAll,
                    holdings,
                    shareFactor
                );
            },
            "GetTopHolders",
            $"ticker: {ticker}"
        );
    }

    private static string RenderTopHoldersTable(
        CommonStock stock,
        string ticker,
        DateOnly targetDate,
        int totalInstitutions,
        long totalSharesAll,
        long totalValueAll,
        List<InstitutionalHolding> holdings,
        decimal shareFactor
    )
    {
        var adjustedTotalShares = SplitAdjustment.AdjustShareCount(totalSharesAll, shareFactor);
        var result = MarkdownTable.Start(
            $"Top institutional holders of {stock.Name} ({ticker}) as of {FormatDate(targetDate)}:",
            $"Showing {holdings.Count} of {totalInstitutions} institutions. Total: "
                + $"{McpFormat.WholeNumber(adjustedTotalShares)} shares, "
                + $"${FormatMillions(totalValueAll)}M value",
            "| # | Institution | Shares | Value ($M) | % of Total |",
            "|---|------------|--------|-----------|-----------|"
        );

        result.AppendNumberedRows(
            holdings,
            (rank, h) =>
            {
                // Ratio computed from raw shares (split-invariant); only the displayed
                // absolute share count is restated onto today's basis.
                var pct = Percentage.Of(h.Shares, totalSharesAll);
                var adjustedShares = SplitAdjustment.AdjustShareCount(h.Shares, shareFactor);
                return $"| {rank} | {h.InstitutionalHolder.Name} | "
                    + $"{McpFormat.WholeNumber(adjustedShares)} | "
                    + $"{FormatMillions(h.Value)} | "
                    + $"{McpFormat.Invariant(pct, "F2")}% |";
            }
        );

        return result.ToString();
    }

    [McpServerTool(Name = "GetOwnershipHistory")]
    [Description(
        "Get the historical trend of institutional ownership for a stock across multiple quarters. Shows how total institutional shares, market value, and number of institutional holders have changed over time based on SEC 13F-HR filings. Use this to understand whether institutional interest in a company is growing or declining."
    )]
    public Task<string> GetOwnershipHistory(
        [Description("Company ticker symbol (e.g., AAPL, MSFT)")] string ticker,
        [Description("Maximum number of quarterly periods to return (default: 8)")]
            int maxPeriods = 8
    )
    {
        return _runner.Execute(
            async () =>
            {
                var (stock, stockError) = await _commonStockRepository.ResolveByTicker(ticker);
                if (stockError != null)
                    return stockError;

                var reportDates = await _holdingRepository
                    .Get13FReportDatesByStock(stock)
                    .Take(McpLimit.Clamp(maxPeriods))
                    .ToListAsync();

                if (reportDates.Count == 0)
                    return $"No institutional holdings history available for {ticker}.";

                return await RenderOwnershipHistory(stock, ticker, reportDates);
            },
            "GetOwnershipHistory",
            $"ticker: {ticker}"
        );
    }

    private async Task<string> RenderOwnershipHistory(
        CommonStock stock,
        string ticker,
        List<DateOnly> reportDates
    )
    {
        var result = MarkdownTable.Start(
            $"Institutional ownership history for {stock.Name} ({ticker}):",
            "| Report Date | Institutions | Total Shares | Total Value ($M) | Change |",
            "|------------|-------------|-------------|-----------------|--------|"
        );

        // Restate each quarter's total shares onto today's split basis before the
        // quarter-over-quarter change so a split between two report dates does not read as a
        // real change in institutional ownership (a 2:1 split would otherwise show +100%).
        var splits = await _stockSplitRepository.GetByStock(stock.Id).ToListAsync();

        long previousShares = 0;
        foreach (var date in reportDates.OrderBy(d => d))
        {
            var holdings = await _holdingRepository.Get13FByStock(stock, date).ToListAsync();
            var totalShares = SplitAdjustment.AdjustShareCount(
                holdings.Sum(h => h.Shares),
                date,
                splits
            );
            var totalValue = holdings.Sum(h => h.Value);
            var institutionCount = holdings.Select(h => h.InstitutionalHolderId).Distinct().Count();

            var change =
                previousShares > 0
                    ? $"{McpFormat.Invariant((double)(totalShares - previousShares) / previousShares * 100, "+0.0;-0.0")}%"
                    : "—";

            result.AppendLine(
                $"| {FormatDate(date)} | {McpFormat.WholeNumber(institutionCount)} | {McpFormat.WholeNumber(totalShares)} | {FormatMillions(totalValue)} | {change} |"
            );

            previousShares = totalShares;
        }

        return result.ToString();
    }

    [McpServerTool(Name = "GetInstitutionPortfolio")]
    [Description(
        "View the stock portfolio of a specific institutional investor (fund manager) from their SEC 13F-HR filing. Shows all tracked stocks held by the institution with share counts and market values. Use this to understand what stocks a particular fund manager or institution is investing in."
    )]
    public Task<string> GetInstitutionPortfolio(
        [Description("Institution name or partial name to search for")] string institutionName,
        [Description("Report date in YYYY-MM-DD format (defaults to latest available)")]
            string reportDate = null,
        [Description("Maximum number of holdings to return (default: 20)")] int maxResults = 20
    )
    {
        return _runner.Execute(
            async () =>
            {
                var holders = await _holderRepository.Search(institutionName).Take(5).ToListAsync();

                if (holders.Count == 0)
                    return $"No institution found matching '{institutionName}'.";

                var holder = holders.First();

                var (targetDate, found) = await TryResolveLatestReportDate(
                    reportDate,
                    _holdingRepository.Get13FReportDatesByHolder(holder)
                );
                if (!found)
                    return $"No holdings data for {holder.Name}.";

                var holdings = await _holdingRepository
                    .GetByHolder(holder, targetDate)
                    .OrderByDescending(h => h.Value)
                    .Take(McpLimit.Clamp(maxResults))
                    .ToListAsync();

                if (holdings.Count == 0)
                    return $"No holdings found for {holder.Name} as of {FormatDate(targetDate)}.";

                // 13F holdings are shown on today's split basis across the platform (matching the
                // web and GetTopHolders), so restate each position's share count by its own
                // stock's post-report-date splits. Value is a paired per-holding dollar figure
                // and stays as reported.
                var splitsByStock = await LoadSplitsByStock(holdings.Select(h => h.CommonStockId));

                return RenderInstitutionPortfolio(holder, targetDate, holdings, splitsByStock);
            },
            "GetInstitutionPortfolio",
            $"institution: {institutionName}"
        );
    }

    private static string RenderInstitutionPortfolio(
        InstitutionalHolder holder,
        DateOnly targetDate,
        List<InstitutionalHolding> holdings,
        IReadOnlyDictionary<Guid, List<StockSplit>> splitsByStock
    )
    {
        var result = MarkdownTable.Start(
            $"Portfolio of {holder.Name} (CIK: {holder.Cik}) as of {FormatDate(targetDate)}:",
            "| # | Ticker | Company | Shares | Value ($M) |",
            "|---|--------|---------|--------|-----------|"
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
                return $"| {rank} | {h.CommonStock.Ticker} | {h.CommonStock.Name} | "
                    + $"{McpFormat.WholeNumber(shares)} | "
                    + $"{FormatMillions(h.Value)} |";
            }
        );

        return result.ToString();
    }

    [McpServerTool(Name = "SearchInstitutions")]
    [Description(
        "Search for institutional investors (fund managers) by name. Returns matching institutions with their SEC CIK number, city, and state/country. Use this to find the correct institution name before calling GetInstitutionPortfolio or to discover which institutions are tracked in the database."
    )]
    public Task<string> SearchInstitutions(
        [Description("Search query — institution name or partial name")] string query,
        [Description("Maximum number of results to return (default: 10)")] int maxResults = 10
    )
    {
        return _runner.Execute(
            async () =>
            {
                maxResults = McpLimit.Clamp(maxResults);

                var holders = await _holderRepository
                    .Search(query)
                    .OrderBy(h => h.Name)
                    .Take(maxResults)
                    .ToListAsync();

                return MarkdownTable.Render(
                    holders,
                    $"No institutions found matching '{query}'.",
                    $"Institutions matching '{query}':",
                    "| Institution | CIK | City | State/Country |",
                    "|------------|-----|------|--------------|",
                    h => $"| {h.Name} | {h.Cik} | {h.City ?? "—"} | {h.StateOrCountry ?? "—"} |"
                );
            },
            "SearchInstitutions",
            $"query: {query}"
        );
    }

    [McpServerTool(Name = "GetTopBuyersSellers")]
    [Description(
        "Get the institutions that moved the needle the most on a stock this quarter — biggest absolute share additions (Top Buyers) and biggest absolute share reductions (Top Sellers) versus the previous 13F report date. Includes new positions (Δ = full position) and sold-out positions (Δ = −prior position). Returns a markdown table with two sections. Use this to surface the most actionable quarterly signal from 13F filings."
    )]
    public Task<string> GetTopBuyersSellers(
        [Description("Company ticker symbol (e.g., AAPL, MSFT)")] string ticker,
        [Description("Report date in YYYY-MM-DD format (defaults to latest available)")]
            string reportDate = null,
        [Description("Maximum number of buyers and sellers to return per section (default: 10)")]
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

                var reportDates = await _holdingRepository
                    .Get13FReportDatesByStock(stock)
                    .ToListAsync();

                var targetDate = TryParseReportDate(reportDate, out var parsed)
                    ? parsed
                    : reportDates.FirstOrDefault();
                if (targetDate == default)
                    return $"No institutional holdings data available for {ticker}.";

                var previousDate = GetPriorReportDate(reportDates, targetDate);

                var currentHoldings = await _holdingRepository
                    .Get13FByStockWithHolder(stock, targetDate)
                    .ToListAsync();
                var previousHoldings = previousDate.HasValue
                    ? await _holdingRepository
                        .Get13FByStockWithHolder(stock, previousDate.Value)
                        .ToListAsync()
                    : [];

                // Aggregate by holder (one holder may file multiple 13F rows per quarter).
                // TODO(#1008): the same aggregation/grouping logic lives in
                // Equibles.Web.Services.HoldingsPositionGrouper. Consolidate into a shared
                // Equibles.Holdings.BusinessLogic project once one exists.
                static Dictionary<Guid, HolderAggregate> AggregateByHolder(
                    List<InstitutionalHolding> holdings
                ) =>
                    holdings
                        .GroupBy(h => h.InstitutionalHolderId)
                        .ToDictionary(
                            g => g.Key,
                            g => new HolderAggregate
                            {
                                Name = g.First().InstitutionalHolder?.Name ?? "Unknown",
                                Shares = g.Sum(h => h.Shares),
                                Value = g.Sum(h => h.Value),
                            }
                        );

                var currentByHolder = AggregateByHolder(currentHoldings);
                var previousByHolder = AggregateByHolder(previousHoldings);

                // Restate each quarter's share counts onto today's split basis (the two
                // quarters sit on different bases if a split fell between them) so Δ Shares
                // and the Prior → New column reflect a real position change, not the split.
                // Δ Value is a dollar figure and is split-invariant — left as reported.
                var splits = await _stockSplitRepository.GetByStock(stock.Id).ToListAsync();
                var currentFactor = SplitAdjustment.ShareCountFactor(targetDate, splits);
                var previousFactor = previousDate.HasValue
                    ? SplitAdjustment.ShareCountFactor(previousDate.Value, splits)
                    : 1m;

                var allHolderIds = currentByHolder.Keys.Union(previousByHolder.Keys);
                var movers = allHolderIds
                    .Select(id =>
                    {
                        currentByHolder.TryGetValue(id, out var c);
                        previousByHolder.TryGetValue(id, out var p);
                        var currentShares = SplitAdjustment.AdjustShareCount(
                            c?.Shares ?? 0,
                            currentFactor
                        );
                        var previousShares = SplitAdjustment.AdjustShareCount(
                            p?.Shares ?? 0,
                            previousFactor
                        );
                        return (
                            Name: c?.Name ?? p?.Name ?? "Unknown",
                            CurrentShares: currentShares,
                            PreviousShares: previousShares,
                            DeltaShares: currentShares - previousShares,
                            DeltaValue: (c?.Value ?? 0) - (p?.Value ?? 0)
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

                return RenderBuyersSellersTable(
                    stock,
                    ticker,
                    targetDate,
                    previousDate,
                    topBuyers,
                    topSellers
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
        )> topSellers
    )
    {
        var result = new StringBuilder();
        result.AppendLine(
            $"Top buyers and sellers of {stock.Name} ({ticker}) as of {FormatDate(targetDate)}"
        );
        if (previousDate.HasValue)
            result.AppendLine(PriorQuarterSubtitle(previousDate.Value));
        result.AppendLine();

        AppendMoverSection(result, "## Top Buyers", "_No buyers this quarter._", topBuyers);
        result.AppendLine();
        AppendMoverSection(result, "## Top Sellers", "_No sellers this quarter._", topSellers);

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
                "| # | Institution | Δ Shares | Δ Value ($M) | Prior → New Shares |",
                "|---|-------------|---------|-------------|------------------|",
                rows,
                (rank, m) =>
                    $"| {rank} | {m.Name} | {FormatSignedShares(m.DeltaShares)} | {FormatSignedMillions(m.DeltaValue)} | {McpFormat.WholeNumber(m.PreviousShares)} → {McpFormat.WholeNumber(m.CurrentShares)} |"
            );
        }
    }

    [McpServerTool(Name = "GetMarketWide13FActivity")]
    [Description(
        "Get the market-wide 13F leaderboards for a given quarter — which stocks were most bought, most sold, most initiated, or most exited across all 13F filers vs the prior quarter. The `bucket` argument selects one of: top-buys (Δ shares > 0 ranked by Δ value desc), top-sells (Δ shares < 0 ranked by Δ value asc), new-positions (stocks ranked by count of filers initiating a position), sold-out-positions (stocks ranked by count of filers exiting). Use this to answer 'what's the consensus 13F move this quarter?'"
    )]
    public Task<string> GetMarketWide13FActivity(
        [Description("Bucket: top-buys, top-sells, new-positions, or sold-out-positions")]
            string bucket,
        [Description("Report date in YYYY-MM-DD format (defaults to latest available)")]
            string reportDate = null,
        [Description("Maximum number of stocks to return (default: 20)")] int maxResults = 20
    )
    {
        return _runner.Execute(
            async () =>
            {
                var normalizedBucket = (bucket ?? string.Empty).Trim().ToLowerInvariant();
                if (!ValidActivityBuckets.Contains(normalizedBucket))
                    return $"Unknown bucket. Use one of: {string.Join(", ", ValidActivityBuckets)}.";

                maxResults = McpLimit.Clamp(maxResults);

                var (targetDate, previousDate, error) = await ResolveMarketActivityDates(
                    reportDate
                );
                if (error != null)
                    return error;

                // Headline + comparison subtitle.
                var result = new StringBuilder();
                result.AppendLine(
                    $"Market-wide 13F **{normalizedBucket}** for {FormatDate(targetDate)}"
                );
                result.AppendLine(PriorQuarterSubtitle(previousDate));
                result.AppendLine();

                if (normalizedBucket is "top-buys" or "top-sells")
                {
                    return await RenderMarketActivityMovers(
                        normalizedBucket,
                        targetDate,
                        previousDate,
                        maxResults,
                        result
                    );
                }
                else
                {
                    return await RenderMarketActivityChurn(
                        normalizedBucket,
                        targetDate,
                        previousDate,
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
        DateOnly Previous,
        string Error
    )> ResolveMarketActivityDates(string reportDate)
    {
        // 13F-only: the prior entry must be the prior QUARTER. The all-filings list now
        // carries daily 13D/G event dates, which would make "previous" the prior day and
        // compare a quarter-end portfolio against a single-day stake.
        var reportDates = await _holdingRepository.Get13FAvailableReportDates().ToListAsync();
        if (reportDates.Count == 0)
            return (default, default, "No 13F holdings data available.");

        var targetDate = TryParseReportDate(reportDate, out var parsed) ? parsed : reportDates[0];
        var targetIndex = reportDates.IndexOf(targetDate);
        if (targetIndex < 0)
            return (
                default,
                default,
                $"Report date {FormatDate(targetDate)} not found. Available dates: {string.Join(", ", reportDates.Take(5).Select(d => FormatDate(d)))}{(reportDates.Count > 5 ? "…" : "")}."
            );

        if (targetIndex >= reportDates.Count - 1)
            return (
                default,
                default,
                $"No prior quarter to compare against for {FormatDate(targetDate)}."
            );

        return (targetDate, reportDates[targetIndex + 1], null);
    }

    private async Task<string> RenderMarketActivityMovers(
        string normalizedBucket,
        DateOnly targetDate,
        DateOnly previousDate,
        int maxResults,
        StringBuilder result
    )
    {
        // Materialize the whole quarter's activity, then restate each stock's share counts onto
        // today's split basis BEFORE bucketing and the Δ Shares column. A split between the two
        // report dates sits the quarters on different bases, so a flat position would otherwise
        // read as a phantom buyer/seller. Δ Value is a dollar figure and is split-invariant (it
        // drives the ordering). The restatement is per-stock, so it cannot translate to SQL.
        var activity = await _holdingRepository
            .GetQuarterlyActivity(targetDate, previousDate)
            .ToListAsync();
        await RestateActivitySharesToTodaysBasis(activity, targetDate, previousDate);

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
        return result.ToString();
    }

    private async Task<string> RenderMarketActivityChurn(
        string normalizedBucket,
        DateOnly targetDate,
        DateOnly previousDate,
        int maxResults,
        StringBuilder result
    )
    {
        var churn = _holdingRepository.GetQuarterlyNewSoldOutPositions(targetDate, previousDate);
        var rows =
            normalizedBucket == "new-positions"
                ? await churn.NewPositions().Take(maxResults).ToListAsync()
                : await churn.SoldOutPositions().Take(maxResults).ToListAsync();
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

    [McpServerTool(Name = "GetMostHeldStocks")]
    [Description(
        "Get the cross-sectional ranking of stocks by institutional 13F breadth for a given quarter. Returns the stocks ranked by number of 13F filers reporting them as a holding (default), or by quarter-over-quarter change in filer count (warming / cooling heat map), or by total reported dollar value. Includes Δ filers vs the prior quarter, total value, Δ value, and the stock's share of the 13F universe. Use this to answer 'which stocks are most owned by institutions right now, and is breadth expanding or contracting?'"
    )]
    public Task<string> GetMostHeldStocks(
        [Description("Report date in YYYY-MM-DD format (defaults to latest available)")]
            string reportDate = null,
        [Description(
            "Sort by: 'filers' (default, # of 13F filers desc), 'filersDelta' (QoQ filer-count delta desc — heat map of warming names), or 'value' (current total reported $ value desc)"
        )]
            string sort = "filers",
        [Description("Maximum number of stocks to return (default: 25)")] int maxResults = 25
    )
    {
        return _runner.Execute(
            async () =>
            {
                var normalizedSort = (sort ?? "filers").Trim().ToLowerInvariant();
                if (!ValidMostHeldSorts.Contains(normalizedSort))
                    return $"Unknown sort. Use one of: {string.Join(", ", ValidMostHeldSorts)}.";

                var (targetDate, previousDate, error) = await ResolveMarketActivityDates(
                    reportDate
                );
                if (error != null)
                    return error;

                var ranking = _holdingRepository.GetMostHeld(targetDate, previousDate);
                ranking = normalizedSort switch
                {
                    "filersdelta" => ranking
                        .OrderByDescending(a => a.CurrentFilerCount - a.PreviousFilerCount)
                        .ThenByDescending(a => a.CurrentFilerCount),
                    "value" => ranking
                        .OrderByDescending(a => a.CurrentValue)
                        .ThenByDescending(a => a.CurrentFilerCount),
                    _ => ranking
                        .OrderByDescending(a => a.CurrentFilerCount)
                        .ThenByDescending(a => a.CurrentValue),
                };
                var rows = await ranking.Take(McpLimit.Clamp(maxResults)).ToListAsync();
                if (rows.Count == 0)
                    return $"No stocks were held by 13F filers as of {FormatDate(targetDate)}.";

                var universeFilers = await _holdingRepository
                    .GetUniqueFilerIds(targetDate)
                    .CountAsync();
                var stocks = await LoadStocksByIds(rows.Select(r => r.CommonStockId).ToList());

                return RenderMostHeldStocksTable(
                    targetDate,
                    previousDate,
                    normalizedSort,
                    universeFilers,
                    rows,
                    stocks
                );
            },
            "GetMostHeldStocks",
            $"sort: {sort}, max: {maxResults}"
        );
    }

    private static string RenderMostHeldStocksTable(
        DateOnly targetDate,
        DateOnly previousDate,
        string sort,
        int universeFilers,
        List<MarketWideStockActivity> rows,
        IDictionary<Guid, CommonStock> stocks
    )
    {
        var result = new StringBuilder();
        result.AppendLine($"Most-held 13F stocks as of {FormatDate(targetDate)}");
        result.AppendLine(
            $"{PriorQuarterSubtitle(previousDate)} · {McpFormat.WholeNumber(universeFilers)} filers in the 13F universe"
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
                var deltaFilers = r.CurrentFilerCount - r.PreviousFilerCount;
                return $"| {rank} | {ticker} | {name} | {McpFormat.WholeNumber(r.CurrentFilerCount)} | {FormatSignedShares(deltaFilers)} | {FormatMillions(r.CurrentValue)} | {FormatSignedMillions(r.DeltaValue)} | {FormatPercent(pct)}% |";
            }
        );
        return result.ToString();
    }

    [McpServerTool(Name = "GetInstitutionSummary")]
    [Description(
        "Get the portfolio summary header for an institutional 13F filer — Reported AUM, position count, top-10 / top-25 concentration, QoQ turnover, and the latest / prior report dates with the count of quarters reported. Use this to answer 'how big and how concentrated is this fund?' or to compare two funds at a glance. Search resolves by institution name (closest match)."
    )]
    public Task<string> GetInstitutionSummary(
        [Description("Institution name (partial or full — first match wins)")]
            string institutionName,
        [Description("Report date in YYYY-MM-DD format (defaults to the holder's latest)")]
            string reportDate = null
    )
    {
        return _runner.Execute(
            async () =>
            {
                var (holder, reportDates, targetDate, error) = await ResolveHolderAndTargetDate(
                    institutionName,
                    reportDate
                );
                if (error != null)
                    return error;

                var previousDate = GetPriorReportDate(reportDates, targetDate);

                var currentHoldings = await _holdingRepository
                    .Get13FByHolder(holder, targetDate)
                    .ToListAsync();
                var previousHoldings = previousDate.HasValue
                    ? await _holdingRepository
                        .Get13FByHolder(holder, previousDate.Value)
                        .ToListAsync()
                    : [];
                var summary = InstitutionPortfolioSummaryCalculator.Calculate(
                    currentHoldings,
                    previousHoldings,
                    reportDates.Count,
                    targetDate,
                    previousDate
                );

                return RenderInstitutionSummary(holder, targetDate, previousDate, summary);
            },
            "GetInstitutionSummary",
            $"institution: {institutionName}"
        );
    }

    private static string RenderInstitutionSummary(
        InstitutionalHolder holder,
        DateOnly targetDate,
        DateOnly? previousDate,
        InstitutionPortfolioSummary summary
    )
    {
        var result = new StringBuilder();
        result.AppendLine($"Portfolio summary — **{holder.Name}** as of {FormatDate(targetDate)}");
        if (previousDate.HasValue)
            result.AppendLine(PriorQuarterSubtitle(previousDate.Value));
        result.AppendLine();
        result.AppendLine("| Metric | Value |");
        result.AppendLine("|--------|-------|");
        result.AppendLine($"| Reported AUM | ${McpFormat.WholeNumber(summary.ReportedAum)} |");
        result.AppendLine($"| # Positions | {McpFormat.WholeNumber(summary.PositionCount)} |");
        result.AppendLine(
            $"| Top 10 concentration | {FormatPercent(summary.Top10ConcentrationPercent)}% |"
        );
        result.AppendLine(
            $"| Top 25 concentration | {FormatPercent(summary.Top25ConcentrationPercent)}% |"
        );
        result.AppendLine($"| QoQ turnover | {FormatPercent(summary.QoQTurnoverPercent)}% |");
        result.AppendLine($"| Quarters reported | {summary.QuartersReported} |");
        result.AppendLine();
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
        List<IndustryAllocationSlice> slices
    )
    {
        var result = new StringBuilder();
        result.AppendLine($"Sector allocation — **{holder.Name}** as of {FormatDate(targetDate)}");
        result.AppendLine();
        if (slices.Count == 0)
        {
            result.AppendLine("_No holdings reported for the selected quarter._");
            return result.ToString();
        }

        result.AppendNumberedTable(
            "| # | Industry | # Positions | Value ($M) | % of Portfolio |",
            "|---|----------|-------------|------------|----------------|",
            slices,
            (rank, s) =>
                $"| {rank} | {s.IndustryName} | {McpFormat.WholeNumber(s.PositionCount)} | {FormatMillions(s.TotalValue)} | {FormatPercent(s.PercentOfPortfolio)}% |"
        );
        return result.ToString();
    }

    [McpServerTool(Name = "GetInstitutionSectorAllocation")]
    [Description(
        "Get an institution's portfolio allocation grouped by industry / sector for its latest 13F report. Returns a markdown table sorted by % of portfolio descending, with stocks lacking an industry classification collapsed into a single 'Unclassified' row at the end. Use this to answer 'is this fund concentrated in tech / energy / generalist?'"
    )]
    public Task<string> GetInstitutionSectorAllocation(
        [Description("Institution name (partial or full — first match wins)")]
            string institutionName,
        [Description("Report date in YYYY-MM-DD format (defaults to the holder's latest)")]
            string reportDate = null
    )
    {
        return _runner.Execute(
            async () =>
            {
                var (holder, _, targetDate, error) = await ResolveHolderAndTargetDate(
                    institutionName,
                    reportDate
                );
                if (error != null)
                    return error;

                var holdings = await _holdingRepository
                    .Get13FByHolder(holder, targetDate)
                    .Include(h => h.CommonStock)
                        .ThenInclude(s => s.Industry)
                    .ToListAsync();
                var slices = IndustryAllocationCalculator.Calculate(holdings);

                return RenderSectorAllocationTable(holder, targetDate, slices);
            },
            "GetInstitutionSectorAllocation",
            $"institution: {institutionName}"
        );
    }

    [McpServerTool(Name = "GetInstitutionQuarterlyActivity")]
    [Description(
        "Get an institution's quarterly position-change activity — Initiated / Increased / Reduced / Exited stocks diffed against the immediately prior quarter. Returns the buckets as one markdown section per bucket, sorted by absolute Δ value desc. Use `bucket` to filter to a single bucket. Use this to answer 'what did this fund do this quarter?'"
    )]
    public Task<string> GetInstitutionQuarterlyActivity(
        [Description("Institution name (partial or full — first match wins)")]
            string institutionName,
        [Description("Report date in YYYY-MM-DD format (defaults to the holder's latest)")]
            string reportDate = null,
        [Description(
            "Filter to a single bucket: initiated, increased, reduced, exited (omit for all four)"
        )]
            string bucket = null,
        [Description("Maximum number of stocks to return per bucket (default: 20)")]
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

                var (holder, holderError) = await ResolveHolderByName(institutionName);
                if (holderError != null)
                    return holderError;

                var reportDates = await _holdingRepository
                    .Get13FReportDatesByHolder(holder)
                    .ToListAsync();
                if (reportDates.Count < 2)
                    return $"{holder.Name} has fewer than two reported quarters — no diff available.";

                var targetDate = ResolveReportDate(reportDate, reportDates);
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
                    maxResults
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
        int maxResults
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
        result.AppendLine();

        var rendered = 0;
        var selectedSections = sections.Where(s =>
            string.IsNullOrEmpty(normalizedBucket) || s.Label.ToLowerInvariant() == normalizedBucket
        );
        foreach (var section in selectedSections)
        {
            var rows = grouped[section.Type]
                .OrderByDescending(r => Math.Abs(r.DeltaValue))
                .Take(maxResults)
                .ToList();
            if (AppendActivitySection(result, section.Label, rows))
                rendered++;
        }

        if (rendered == 0)
            result.AppendLine("_No matching buckets._");
        return result.ToString();
    }

    private static bool AppendActivitySection(
        StringBuilder result,
        string label,
        List<StockPositionChange> rows
    )
    {
        result.AppendLine($"## {label}");
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

    [McpServerTool(Name = "GetFundOverlap")]
    [Description(
        "Get the 13F portfolio overlap between two institutions for their latest common report date — Jaccard similarity, dollar-weighted overlap, and a side-by-side table of stocks with per-fund shares + percent of portfolio. Use this to answer 'do these two funds own the same stocks?' or 'where do their portfolios diverge?'"
    )]
    public Task<string> GetFundOverlap(
        [Description("First institution name (partial or full — first match wins)")]
            string institutionName1,
        [Description("Second institution name (partial or full — first match wins)")]
            string institutionName2,
        [Description("Report date in YYYY-MM-DD format (defaults to latest common quarter)")]
            string reportDate = null,
        [Description("Maximum number of stocks to return (default: 30)")] int maxResults = 30
    )
    {
        return _runner.Execute(
            async () =>
            {
                var (holder1, holder1Error) = await ResolveHolderByName(institutionName1);
                if (holder1Error != null)
                    return holder1Error;
                var (holder2, holder2Error) = await ResolveHolderByName(institutionName2);
                if (holder2Error != null)
                    return holder2Error;

                var (selected, error) = await ResolveCommonReportDate(holder1, holder2, reportDate);
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

                return RenderOverlapTable(holder1, holder2, selected, overlap, maxResults);
            },
            "GetFundOverlap",
            $"funds: {institutionName1}, {institutionName2}"
        );
    }

    private async Task<(DateOnly Selected, string Error)> ResolveCommonReportDate(
        InstitutionalHolder holder1,
        InstitutionalHolder holder2,
        string reportDate
    )
    {
        var common = await ComputeCommonReportDates([holder1, holder2]);
        if (common.Count == 0)
            return (default, $"{holder1.Name} and {holder2.Name} share no common report dates.");

        return (ResolveReportDate(reportDate, common), null);
    }

    private async Task<List<DateOnly>> ComputeCommonReportDates(IList<InstitutionalHolder> holders)
    {
        var perHolder = new List<List<DateOnly>>(holders.Count);
        foreach (var holder in holders)
            perHolder.Add(await _holdingRepository.Get13FReportDatesByHolder(holder).ToListAsync());

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
        int maxResults
    )
    {
        var result = MarkdownTable.Start(
            $"Portfolio overlap — **{holder1.Name}** vs **{holder2.Name}** as of {FormatDate(selected)}",
            "| Metric | Value |",
            "|--------|-------|"
        );
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
        return result.ToString();
    }

    [McpServerTool(Name = "GetConsensusHoldings")]
    [Description(
        "Get the consensus / combined portfolio of 2-25 institutions for their latest common report date. Returns stocks ranked by how many of the supplied funds hold them (descending), then by combined value. Filter by `minFunds` to only show stocks held by at least that many funds. Use this to answer 'what do these funds agree on?' or 'show me the top picks across these N investors combined.'"
    )]
    public Task<string> GetConsensusHoldings(
        [Description(
            "Comma- or semicolon-separated institution names (partial or full — first match wins per name). 2-25 names."
        )]
            string institutionNames,
        [Description("Report date in YYYY-MM-DD format (defaults to latest common quarter)")]
            string reportDate = null,
        [Description("Minimum number of funds a stock must be held by to appear (default: 1)")]
            int minFunds = 1,
        [Description("Maximum number of stocks to return (default: 30)")] int maxResults = 30
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
                foreach (var name in names)
                {
                    var holder = await FindHolderByName(name);
                    if (holder == null)
                        missing.Add(name);
                    else
                        holders.Add(holder);
                }
                if (holders.Count < 2)
                    return $"Could not resolve enough institutions. Missing: {string.Join(", ", missing)}.";

                var common = await ComputeCommonReportDates(holders);
                if (common.Count == 0)
                    return "The selected institutions share no common report dates.";

                var selected = ResolveReportDate(reportDate, common);

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

                var rowsWithConsensus = overlap
                    .Rows.Select(r => (Row: r, HeldBy: r.Slices.Count(s => s.Value > 0)))
                    .Where(x => x.HeldBy >= Math.Max(1, minFunds))
                    .OrderByDescending(x => x.HeldBy)
                    .ThenByDescending(x => x.Row.CombinedValue)
                    .Take(maxResults)
                    .ToList();

                return RenderConsensusHoldingsTable(holders, missing, selected, rowsWithConsensus);
            },
            "GetConsensusHoldings",
            $"names: {institutionNames}"
        );
    }

    private static string RenderConsensusHoldingsTable(
        List<InstitutionalHolder> holders,
        List<string> missing,
        DateOnly selected,
        List<(FundOverlapRow Row, int HeldBy)> rowsWithConsensus
    )
    {
        var result = new StringBuilder();
        result.AppendLine(
            $"Consensus holdings — **{holders.Count} funds** as of {FormatDate(selected)}"
        );
        if (missing.Count > 0)
            result.AppendLine($"_Could not resolve: {string.Join(", ", missing)}._");
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
        return result.ToString();
    }

    private Task<InstitutionalHolder> FindHolderByName(string name) =>
        _holderRepository
            .Search(name ?? string.Empty)
            .OrderBy(h => h.Name.Length)
            .ThenBy(h => h.Name)
            .FirstOrDefaultAsync();

    private async Task<(InstitutionalHolder Holder, string Error)> ResolveHolderByName(string name)
    {
        var holder = await FindHolderByName(name);
        if (holder == null)
            return (null, $"No institution found matching '{name}'.");
        return (holder, null);
    }

    private Task<List<InstitutionalHolding>> LoadHoldingsByHolderWithStock(
        InstitutionalHolder holder,
        DateOnly reportDate
    ) => _holdingRepository.GetByHolderWithStock(holder, reportDate).ToListAsync();

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

    // Restates each activity row's current/previous share counts onto today's split basis
    // (current at the target quarter, previous at the prior quarter) from each row's own
    // stock's splits. These rows are query projections, not tracked entities, so mutating them
    // in place is safe; Δ Shares recomputes from the restated counts.
    private async Task RestateActivitySharesToTodaysBasis(
        IReadOnlyList<MarketWideStockActivity> activity,
        DateOnly currentDate,
        DateOnly previousDate
    )
    {
        var splitsByStock = await LoadSplitsByStock(activity.Select(a => a.CommonStockId));
        foreach (var a in activity)
        {
            var splits = SplitsFor(splitsByStock, a.CommonStockId);
            a.CurrentShares = SplitAdjustment.AdjustShareCount(a.CurrentShares, currentDate, splits);
            a.PreviousShares = SplitAdjustment.AdjustShareCount(
                a.PreviousShares,
                previousDate,
                splits
            );
        }
    }

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
        string Error
    )> ResolveHolderAndTargetDate(string institutionName, string reportDate)
    {
        var (holder, holderError) = await ResolveHolderByName(institutionName);
        if (holderError != null)
            return (null, null, default, holderError);

        var reportDates = await _holdingRepository.Get13FReportDatesByHolder(holder).ToListAsync();
        if (reportDates.Count == 0)
            return (holder, null, default, $"No 13F holdings reported by {holder.Name}.");

        return (holder, reportDates, ResolveReportDate(reportDate, reportDates), null);
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

    private static DateOnly ResolveReportDate(string input, IReadOnlyList<DateOnly> validDates) =>
        TryParseReportDate(input, out var parsed) && validDates.Contains(parsed)
            ? parsed
            : validDates[0];

    // Report-date lists are newest-first, so the prior quarter sits at the next index.
    // Returns null when the target is absent from the list or is already the oldest quarter.
    private static DateOnly? GetPriorReportDate(List<DateOnly> reportDates, DateOnly targetDate)
    {
        var index = reportDates.IndexOf(targetDate);
        if (index < 0 || index >= reportDates.Count - 1)
            return null;
        return reportDates[index + 1];
    }

    private static async Task<(DateOnly Date, bool Found)> TryResolveLatestReportDate(
        string input,
        IQueryable<DateOnly> dateSource
    )
    {
        if (TryParseReportDate(input, out var parsed))
            return (parsed, true);

        var latest = await dateSource.FirstOrDefaultAsync();
        return (latest, latest != default);
    }

    private class HolderAggregate
    {
        public string Name { get; set; }
        public long Shares { get; set; }
        public long Value { get; set; }
    }

    // Thin forwarder so existing reflection-based normalization tests still find the method.
    private Task<(CommonStock Stock, string Error)> ResolveStockByTicker(string ticker) =>
        _commonStockRepository.ResolveByTicker(ticker);
}
