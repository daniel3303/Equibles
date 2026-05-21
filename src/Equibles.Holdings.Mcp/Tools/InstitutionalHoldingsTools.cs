using System.ComponentModel;
using System.Text;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Repositories;
using Equibles.Holdings.Repositories.Models;
using Equibles.Mcp;
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
    private readonly ErrorManager _errorManager;
    private readonly ILogger<InstitutionalHoldingsTools> _logger;

    public InstitutionalHoldingsTools(
        InstitutionalHoldingRepository holdingRepository,
        InstitutionalHolderRepository holderRepository,
        CommonStockRepository commonStockRepository,
        ErrorManager errorManager,
        ILogger<InstitutionalHoldingsTools> logger
    )
    {
        _holdingRepository = holdingRepository;
        _holderRepository = holderRepository;
        _commonStockRepository = commonStockRepository;
        _errorManager = errorManager;
        _logger = logger;
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
        return Execute(
            async () =>
            {
                var stock = await _commonStockRepository.GetByTicker(ticker);
                if (stock == null)
                    return $"Stock '{ticker}' not found.";

                var (targetDate, found) = await TryResolveLatestReportDate(
                    reportDate,
                    GetReportDatesByStock(stock)
                );
                if (!found)
                    return $"No institutional holdings data available for {ticker}.";

                var allHoldings = _holdingRepository.GetByStock(stock, targetDate);
                var totalInstitutions = await allHoldings
                    .Select(h => h.InstitutionalHolderId)
                    .Distinct()
                    .CountAsync();
                var totalSharesAll = await allHoldings.SumAsync(h => h.Shares);
                var totalValueAll = await allHoldings.SumAsync(h => h.Value);

                var holdings = await allHoldings
                    .OrderByDescending(h => h.Shares)
                    .Take(maxResults)
                    .ToListAsync();

                if (holdings.Count == 0)
                    return $"No institutional holdings found for {ticker} as of {targetDate:yyyy-MM-dd}.";

                return RenderTopHoldersTable(
                    stock,
                    ticker,
                    targetDate,
                    totalInstitutions,
                    totalSharesAll,
                    totalValueAll,
                    holdings
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
        List<InstitutionalHolding> holdings
    )
    {
        var result = new StringBuilder();
        result.AppendLine(
            $"Top institutional holders of {stock.Name} ({ticker}) as of {targetDate:yyyy-MM-dd}:"
        );
        result.AppendLine(
            $"Showing {holdings.Count} of {totalInstitutions} institutions. Total: {totalSharesAll:N0} shares, ${totalValueAll / 1_000_000m:N1}M value"
        );
        result.AppendLine();
        result.AppendLine("| # | Institution | Shares | Value ($M) | % of Total |");
        result.AppendLine("|---|------------|--------|-----------|-----------|");

        for (var i = 0; i < holdings.Count; i++)
        {
            var h = holdings[i];
            var pct = totalSharesAll > 0 ? (double)h.Shares / totalSharesAll * 100 : 0;
            result.AppendLine(
                $"| {i + 1} | {h.InstitutionalHolder.Name} | {h.Shares:N0} | {h.Value / 1_000_000m:N1} | {pct:F2}% |"
            );
        }

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
        return Execute(
            async () =>
            {
                var stock = await _commonStockRepository.GetByTicker(ticker);
                if (stock == null)
                    return $"Stock '{ticker}' not found.";

                var reportDates = await GetReportDatesByStock(stock).Take(maxPeriods).ToListAsync();

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
        var result = new StringBuilder();
        result.AppendLine($"Institutional ownership history for {stock.Name} ({ticker}):");
        result.AppendLine();
        result.AppendLine(
            "| Report Date | Institutions | Total Shares | Total Value ($M) | Change |"
        );
        result.AppendLine("|------------|-------------|-------------|-----------------|--------|");

        long previousShares = 0;
        foreach (var date in reportDates.OrderBy(d => d))
        {
            var holdings = await _holdingRepository.GetByStock(stock, date).ToListAsync();
            var totalShares = holdings.Sum(h => h.Shares);
            var totalValue = holdings.Sum(h => h.Value);
            var institutionCount = holdings.Select(h => h.InstitutionalHolderId).Distinct().Count();

            var change =
                previousShares > 0
                    ? $"{(double)(totalShares - previousShares) / previousShares * 100:+0.0;-0.0}%"
                    : "—";

            result.AppendLine(
                $"| {date:yyyy-MM-dd} | {institutionCount:N0} | {totalShares:N0} | {totalValue / 1_000_000m:N1} | {change} |"
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
        return Execute(
            async () =>
            {
                var holders = await _holderRepository.Search(institutionName).Take(5).ToListAsync();

                if (holders.Count == 0)
                    return $"No institution found matching '{institutionName}'.";

                var holder = holders.First();

                var (targetDate, found) = await TryResolveLatestReportDate(
                    reportDate,
                    GetReportDatesByHolder(holder)
                );
                if (!found)
                    return $"No holdings data for {holder.Name}.";

                var holdings = await _holdingRepository
                    .GetByHolder(holder, targetDate)
                    .OrderByDescending(h => h.Value)
                    .Take(maxResults)
                    .ToListAsync();

                if (holdings.Count == 0)
                    return $"No holdings found for {holder.Name} as of {targetDate:yyyy-MM-dd}.";

                return RenderInstitutionPortfolio(holder, targetDate, holdings);
            },
            "GetInstitutionPortfolio",
            $"institution: {institutionName}"
        );
    }

    private static string RenderInstitutionPortfolio(
        InstitutionalHolder holder,
        DateOnly targetDate,
        List<InstitutionalHolding> holdings
    )
    {
        var result = new StringBuilder();
        result.AppendLine(
            $"Portfolio of {holder.Name} (CIK: {holder.Cik}) as of {targetDate:yyyy-MM-dd}:"
        );
        result.AppendLine();
        result.AppendLine("| # | Ticker | Company | Shares | Value ($M) |");
        result.AppendLine("|---|--------|---------|--------|-----------|");

        for (var i = 0; i < holdings.Count; i++)
        {
            var h = holdings[i];
            result.AppendLine(
                $"| {i + 1} | {h.CommonStock.Ticker} | {h.CommonStock.Name} | {h.Shares:N0} | {h.Value / 1_000_000m:N1} |"
            );
        }

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
        return Execute(
            async () =>
            {
                var holders = await _holderRepository
                    .Search(query)
                    .OrderBy(h => h.Name)
                    .Take(maxResults)
                    .ToListAsync();

                if (holders.Count == 0)
                    return $"No institutions found matching '{query}'.";

                var result = new StringBuilder();
                result.AppendLine($"Institutions matching '{query}':");
                result.AppendLine();
                result.AppendLine("| Institution | CIK | City | State/Country |");
                result.AppendLine("|------------|-----|------|--------------|");

                foreach (var h in holders)
                {
                    result.AppendLine(
                        $"| {h.Name} | {h.Cik} | {h.City ?? "—"} | {h.StateOrCountry ?? "—"} |"
                    );
                }

                return result.ToString();
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
        return Execute(
            async () =>
            {
                var stock = await _commonStockRepository.GetByTicker(ticker);
                if (stock == null)
                    return $"Stock '{ticker}' not found.";

                var reportDates = await GetReportDatesByStock(stock).ToListAsync();

                var targetDate = TryParseReportDate(reportDate, out var parsed)
                    ? parsed
                    : reportDates.FirstOrDefault();
                if (targetDate == default)
                    return $"No institutional holdings data available for {ticker}.";

                var selectedIndex = reportDates.IndexOf(targetDate);
                var previousDate =
                    selectedIndex >= 0 && selectedIndex < reportDates.Count - 1
                        ? reportDates[selectedIndex + 1]
                        : (DateOnly?)null;

                var currentHoldings = await _holdingRepository
                    .GetByStock(stock, targetDate)
                    .Include(h => h.InstitutionalHolder)
                    .ToListAsync();
                var previousHoldings = previousDate.HasValue
                    ? await _holdingRepository
                        .GetByStock(stock, previousDate.Value)
                        .Include(h => h.InstitutionalHolder)
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

                var allHolderIds = currentByHolder.Keys.Union(previousByHolder.Keys);
                var movers = allHolderIds
                    .Select(id =>
                    {
                        currentByHolder.TryGetValue(id, out var c);
                        previousByHolder.TryGetValue(id, out var p);
                        return (
                            Name: c?.Name ?? p?.Name ?? "Unknown",
                            CurrentShares: c?.Shares ?? 0,
                            PreviousShares: p?.Shares ?? 0,
                            DeltaShares: (c?.Shares ?? 0) - (p?.Shares ?? 0),
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
                    return $"No quarter-over-quarter movement found for {stock.Name} ({ticker}) as of {targetDate:yyyy-MM-dd}.";

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
            $"Top buyers and sellers of {stock.Name} ({ticker}) as of {targetDate:yyyy-MM-dd}"
        );
        if (previousDate.HasValue)
            result.AppendLine($"vs prior quarter {previousDate.Value:yyyy-MM-dd}");
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
            sb.AppendLine("| # | Institution | Δ Shares | Δ Value ($M) | Prior → New Shares |");
            sb.AppendLine("|---|-------------|---------|-------------|------------------|");
            for (var i = 0; i < rows.Count; i++)
            {
                var m = rows[i];
                // `+` for positive deltas; N0 already emits `-` for negatives.
                var sign = m.DeltaShares > 0 ? "+" : "";
                sb.AppendLine(
                    $"| {i + 1} | {m.Name} | {sign}{m.DeltaShares:N0} | {m.DeltaValue / 1_000_000m:+#,##0.0;-#,##0.0;0.0} | {m.PreviousShares:N0} → {m.CurrentShares:N0} |"
                );
            }
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
        return Execute(
            async () =>
            {
                var normalizedBucket = (bucket ?? string.Empty).Trim().ToLowerInvariant();
                if (!ValidActivityBuckets.Contains(normalizedBucket))
                    return $"Unknown bucket. Use one of: {string.Join(", ", ValidActivityBuckets)}.";

                var (targetDate, previousDate, error) = await ResolveMarketActivityDates(
                    reportDate
                );
                if (error != null)
                    return error;

                // Headline + comparison subtitle.
                var result = new StringBuilder();
                result.AppendLine(
                    $"Market-wide 13F **{normalizedBucket}** for {targetDate:yyyy-MM-dd}"
                );
                result.AppendLine($"vs prior quarter {previousDate:yyyy-MM-dd}");
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
        var reportDates = await _holdingRepository
            .GetAvailableReportDates()
            .Distinct()
            .OrderByDescending(d => d)
            .ToListAsync();
        if (reportDates.Count == 0)
            return (default, default, "No 13F holdings data available.");

        var targetDate = TryParseReportDate(reportDate, out var parsed) ? parsed : reportDates[0];
        var targetIndex = reportDates.IndexOf(targetDate);
        if (targetIndex < 0)
            return (
                default,
                default,
                $"Report date {targetDate:yyyy-MM-dd} not found. Available dates: {string.Join(", ", reportDates.Take(5).Select(d => d.ToString("yyyy-MM-dd")))}{(reportDates.Count > 5 ? "…" : "")}."
            );

        if (targetIndex >= reportDates.Count - 1)
            return (
                default,
                default,
                $"No prior quarter to compare against for {targetDate:yyyy-MM-dd}."
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
        var activity = _holdingRepository.GetQuarterlyActivity(targetDate, previousDate);
        var movers = activity.Where(a => a.CurrentShares != a.PreviousShares);
        var rows =
            normalizedBucket == "top-buys"
                ? await movers
                    .Where(a => a.CurrentShares > a.PreviousShares)
                    .OrderByDescending(a => a.CurrentValue - a.PreviousValue)
                    .Take(maxResults)
                    .ToListAsync()
                : await movers
                    .Where(a => a.CurrentShares < a.PreviousShares)
                    .OrderBy(a => a.CurrentValue - a.PreviousValue)
                    .Take(maxResults)
                    .ToListAsync();
        if (rows.Count == 0)
            return result + "_No stocks moved in this direction this quarter._";

        var stockIds = rows.Select(r => r.CommonStockId).ToList();
        var stocks = await _commonStockRepository
            .GetAll()
            .Where(s => stockIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id);

        result.AppendLine("| # | Ticker | Company | Δ Shares | Δ Value ($M) |");
        result.AppendLine("|---|--------|---------|---------|-------------|");
        for (var i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            stocks.TryGetValue(r.CommonStockId, out var s);
            var sign = r.DeltaShares > 0 ? "+" : "";
            result.AppendLine(
                $"| {i + 1} | {s?.Ticker ?? "—"} | {s?.Name ?? "Unknown"} | {sign}{r.DeltaShares:N0} | {r.DeltaValue / 1_000_000m:+#,##0.0;-#,##0.0;0.0} |"
            );
        }
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
                ? await churn
                    .Where(c => c.NewFilerCount > 0)
                    .OrderByDescending(c => c.NewFilerCount)
                    .Take(maxResults)
                    .ToListAsync()
                : await churn
                    .Where(c => c.SoldOutFilerCount > 0)
                    .OrderByDescending(c => c.SoldOutFilerCount)
                    .Take(maxResults)
                    .ToListAsync();
        if (rows.Count == 0)
            return result + "_No stocks in this bucket this quarter._";

        var stockIds = rows.Select(r => r.CommonStockId).ToList();
        var stocks = await _commonStockRepository
            .GetAll()
            .Where(s => stockIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id);

        var label = normalizedBucket == "new-positions" ? "# Filers Initiated" : "# Filers Exited";
        result.AppendLine($"| # | Ticker | Company | {label} |");
        result.AppendLine("|---|--------|---------|-------------|");
        for (var i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            stocks.TryGetValue(r.CommonStockId, out var s);
            var count = normalizedBucket == "new-positions" ? r.NewFilerCount : r.SoldOutFilerCount;
            result.AppendLine(
                $"| {i + 1} | {s?.Ticker ?? "—"} | {s?.Name ?? "Unknown"} | {count:N0} |"
            );
        }
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
        return Execute(
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
                var rows = await ranking.Take(maxResults).ToListAsync();
                if (rows.Count == 0)
                    return $"No stocks were held by 13F filers as of {targetDate:yyyy-MM-dd}.";

                var universeFilers = await _holdingRepository
                    .GetUniqueFilerIds(targetDate)
                    .CountAsync();
                var stockIds = rows.Select(r => r.CommonStockId).ToList();
                var stocks = await _commonStockRepository
                    .GetAll()
                    .Where(s => stockIds.Contains(s.Id))
                    .ToDictionaryAsync(s => s.Id);

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
        result.AppendLine($"Most-held 13F stocks as of {targetDate:yyyy-MM-dd}");
        result.AppendLine(
            $"vs prior quarter {previousDate:yyyy-MM-dd} · {universeFilers:N0} filers in the 13F universe"
        );
        result.AppendLine($"Sorted by: {sort}");
        result.AppendLine();
        result.AppendLine(
            "| # | Ticker | Company | # Filers | Δ Filers (QoQ) | Total $ Value ($M) | Δ $ Value ($M) | % of 13F Universe |"
        );
        result.AppendLine(
            "|---|--------|---------|----------|----------------|--------------------|----------------|-------------------|"
        );
        for (var i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            stocks.TryGetValue(r.CommonStockId, out var s);
            var pct = universeFilers > 0 ? (double)r.CurrentFilerCount / universeFilers * 100.0 : 0;
            var deltaFilers = r.CurrentFilerCount - r.PreviousFilerCount;
            var filerSign = deltaFilers > 0 ? "+" : "";
            result.AppendLine(
                $"| {i + 1} | {s?.Ticker ?? "—"} | {s?.Name ?? "Unknown"} | {r.CurrentFilerCount:N0} | {filerSign}{deltaFilers:N0} | {r.CurrentValue / 1_000_000m:N1} | {r.DeltaValue / 1_000_000m:+#,##0.0;-#,##0.0;0.0} | {pct:F1}% |"
            );
        }
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
        return Execute(
            async () =>
            {
                var (holder, reportDates, targetDate, error) = await ResolveHolderAndTargetDate(
                    institutionName,
                    reportDate
                );
                if (error != null)
                    return error;

                var targetIndex = reportDates.IndexOf(targetDate);
                var previousDate =
                    targetIndex < reportDates.Count - 1
                        ? reportDates[targetIndex + 1]
                        : (DateOnly?)null;

                var currentHoldings = await _holdingRepository
                    .GetByHolder(holder, targetDate)
                    .ToListAsync();
                var previousHoldings = previousDate.HasValue
                    ? await _holdingRepository.GetByHolder(holder, previousDate.Value).ToListAsync()
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
        result.AppendLine($"Portfolio summary — **{holder.Name}** as of {targetDate:yyyy-MM-dd}");
        if (previousDate.HasValue)
            result.AppendLine($"vs prior quarter {previousDate.Value:yyyy-MM-dd}");
        result.AppendLine();
        result.AppendLine("| Metric | Value |");
        result.AppendLine("|--------|-------|");
        result.AppendLine($"| Reported AUM | ${summary.ReportedAum:N0} |");
        result.AppendLine($"| # Positions | {summary.PositionCount:N0} |");
        result.AppendLine($"| Top 10 concentration | {summary.Top10ConcentrationPercent:F1}% |");
        result.AppendLine($"| Top 25 concentration | {summary.Top25ConcentrationPercent:F1}% |");
        result.AppendLine($"| QoQ turnover | {summary.QoQTurnoverPercent:F1}% |");
        result.AppendLine($"| Quarters reported | {summary.QuartersReported} |");
        result.AppendLine();
        result.AppendLine(
            "_QoQ turnover = (Σ |Δ shares × current price proxy|) / (2 × AUM), where the per-share price proxy is the current quarter's Value / Shares._"
        );

        return result.ToString();
    }

    private static string RenderSectorAllocationTable(
        InstitutionalHolder holder,
        DateOnly targetDate,
        List<IndustryAllocationSlice> slices
    )
    {
        var result = new StringBuilder();
        result.AppendLine($"Sector allocation — **{holder.Name}** as of {targetDate:yyyy-MM-dd}");
        result.AppendLine();
        if (slices.Count == 0)
        {
            result.AppendLine("_No holdings reported for the selected quarter._");
            return result.ToString();
        }

        result.AppendLine("| # | Industry | # Positions | Value ($M) | % of Portfolio |");
        result.AppendLine("|---|----------|-------------|------------|----------------|");
        for (var i = 0; i < slices.Count; i++)
        {
            var s = slices[i];
            result.AppendLine(
                $"| {i + 1} | {s.IndustryName} | {s.PositionCount:N0} | {s.TotalValue / 1_000_000m:N1} | {s.PercentOfPortfolio:F1}% |"
            );
        }
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
        return Execute(
            async () =>
            {
                var (holder, _, targetDate, error) = await ResolveHolderAndTargetDate(
                    institutionName,
                    reportDate
                );
                if (error != null)
                    return error;

                var holdings = await _holdingRepository
                    .GetByHolder(holder, targetDate)
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
        return Execute(
            async () =>
            {
                var normalizedBucket = bucket?.Trim().ToLowerInvariant();
                if (
                    !string.IsNullOrEmpty(normalizedBucket)
                    && !ValidInstitutionActivityBuckets.Contains(normalizedBucket)
                )
                    return "Unknown bucket. Use one of: initiated, increased, reduced, exited (or omit).";

                var holder = await FindHolderByName(institutionName);
                if (holder == null)
                    return $"No institution found matching '{institutionName}'.";

                var reportDates = await GetReportDatesByHolder(holder).ToListAsync();
                if (reportDates.Count < 2)
                    return $"{holder.Name} has fewer than two reported quarters — no diff available.";

                var targetDate = ResolveReportDate(reportDate, reportDates);
                var targetIndex = reportDates.IndexOf(targetDate);
                if (targetIndex >= reportDates.Count - 1)
                    return $"{targetDate:yyyy-MM-dd} is the oldest reported quarter for {holder.Name} — no prior to compare against.";

                var priorDate = reportDates[targetIndex + 1];
                var currentHoldings = await LoadHoldingsByHolderWithStock(holder, targetDate);
                var previousHoldings = await LoadHoldingsByHolderWithStock(holder, priorDate);
                var grouped = HolderQuarterlyActivityCalculator.Group(
                    currentHoldings,
                    previousHoldings
                );

                return RenderQuarterlyActivity(
                    holder,
                    targetDate,
                    priorDate,
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
        result.AppendLine($"Quarterly activity — **{holder.Name}** as of {targetDate:yyyy-MM-dd}");
        result.AppendLine($"vs prior quarter {priorDate:yyyy-MM-dd}");
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
        result.AppendLine("| # | Ticker | Company | Prior | New | Δ Shares | Δ Value ($M) |");
        result.AppendLine("|---|--------|---------|-------|-----|---------|-------------|");
        for (var i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            var sign = r.DeltaShares > 0 ? "+" : "";
            result.AppendLine(
                $"| {i + 1} | {r.Ticker} | {r.Name} | {r.PreviousShares:N0} | {r.CurrentShares:N0} | {sign}{r.DeltaShares:N0} | {r.DeltaValue / 1_000_000m:+#,##0.0;-#,##0.0;0.0} |"
            );
        }
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
        return Execute(
            async () =>
            {
                var holder1 = await FindHolderByName(institutionName1);
                if (holder1 == null)
                    return $"No institution found matching '{institutionName1}'.";
                var holder2 = await FindHolderByName(institutionName2);
                if (holder2 == null)
                    return $"No institution found matching '{institutionName2}'.";

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
        var dates1 = await _holdingRepository
            .GetHistoryByHolder(holder1)
            .Select(h => h.ReportDate)
            .Distinct()
            .ToListAsync();
        var dates2 = await _holdingRepository
            .GetHistoryByHolder(holder2)
            .Select(h => h.ReportDate)
            .Distinct()
            .ToListAsync();
        var common = dates1.Intersect(dates2).OrderByDescending(d => d).ToList();
        if (common.Count == 0)
            return (default, $"{holder1.Name} and {holder2.Name} share no common report dates.");

        return (ResolveReportDate(reportDate, common), null);
    }

    private static string RenderOverlapTable(
        InstitutionalHolder holder1,
        InstitutionalHolder holder2,
        DateOnly selected,
        FundOverlapResult overlap,
        int maxResults
    )
    {
        var result = new StringBuilder();
        result.AppendLine(
            $"Portfolio overlap — **{holder1.Name}** vs **{holder2.Name}** as of {selected:yyyy-MM-dd}"
        );
        result.AppendLine();
        result.AppendLine("| Metric | Value |");
        result.AppendLine("|--------|-------|");
        result.AppendLine($"| Union positions | {overlap.UnionPositionCount:N0} |");
        result.AppendLine($"| Shared positions | {overlap.IntersectionPositionCount:N0} |");
        result.AppendLine($"| Jaccard similarity | {overlap.JaccardSimilarityPercent:F1}% |");
        result.AppendLine($"| $-weighted overlap | {overlap.DollarWeightedOverlapPercent:F1}% |");
        result.AppendLine();

        if (overlap.Rows.Count == 0)
        {
            result.AppendLine("_Neither fund reports any positions for this date._");
            return result.ToString();
        }

        result.AppendLine(
            "| # | Ticker | Company | A Shares | A % | B Shares | B % | Combined ($M) |"
        );
        result.AppendLine(
            "|---|--------|---------|---------|-----|---------|-----|---------------|"
        );
        var rendered = overlap.Rows.Take(maxResults).ToList();
        for (var i = 0; i < rendered.Count; i++)
        {
            var row = rendered[i];
            var a = row.Slices[0];
            var b = row.Slices[1];
            result.AppendLine(
                $"| {i + 1} | {row.Ticker} | {row.Name} | {(a.Shares > 0 ? a.Shares.ToString("N0") : "—")} | {(a.Value > 0 ? a.PercentOfPortfolio.ToString("F1") + "%" : "—")} | {(b.Shares > 0 ? b.Shares.ToString("N0") : "—")} | {(b.Value > 0 ? b.PercentOfPortfolio.ToString("F1") + "%" : "—")} | {row.CombinedValue / 1_000_000m:N1} |"
            );
        }
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
        return Execute(
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

                var perHolderDates = new List<List<DateOnly>>();
                foreach (var holder in holders)
                {
                    var dates = await GetReportDatesByHolder(holder).ToListAsync();
                    perHolderDates.Add(dates);
                }
                var common = perHolderDates
                    .Skip(1)
                    .Aggregate(
                        (IEnumerable<DateOnly>)perHolderDates[0],
                        (acc, next) => acc.Intersect(next)
                    )
                    .OrderByDescending(d => d)
                    .ToList();
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
            $"Consensus holdings — **{holders.Count} funds** as of {selected:yyyy-MM-dd}"
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

        result.AppendLine("| # | Ticker | Company | # Funds | Combined ($M) |");
        result.AppendLine("|---|--------|---------|---------|---------------|");
        for (var i = 0; i < rowsWithConsensus.Count; i++)
        {
            var x = rowsWithConsensus[i];
            result.AppendLine(
                $"| {i + 1} | {x.Row.Ticker} | {x.Row.Name} | {x.HeldBy}/{holders.Count} | {x.Row.CombinedValue / 1_000_000m:N1} |"
            );
        }
        return result.ToString();
    }

    private Task<string> Execute(Func<Task<string>> work, string toolName, string context) =>
        McpToolExecutor.Execute(work, _logger, toolName, context, ReportError);

    private Task ReportError(string toolName, string message, string stackTrace, string context)
    {
        return _errorManager.Create(ErrorSource.McpTool, toolName, message, stackTrace, context);
    }

    private Task<InstitutionalHolder> FindHolderByName(string name) =>
        _holderRepository.Search(name ?? string.Empty).OrderBy(h => h.Name).FirstOrDefaultAsync();

    private Task<List<InstitutionalHolding>> LoadHoldingsByHolderWithStock(
        InstitutionalHolder holder,
        DateOnly reportDate
    ) =>
        _holdingRepository
            .GetByHolder(holder, reportDate)
            .Include(h => h.CommonStock)
            .ToListAsync();

    private async Task<(
        InstitutionalHolder Holder,
        List<DateOnly> ReportDates,
        DateOnly TargetDate,
        string Error
    )> ResolveHolderAndTargetDate(string institutionName, string reportDate)
    {
        var holder = await FindHolderByName(institutionName);
        if (holder == null)
            return (null, null, default, $"No institution found matching '{institutionName}'.");

        var reportDates = await GetReportDatesByHolder(holder).ToListAsync();
        if (reportDates.Count == 0)
            return (holder, null, default, $"No 13F holdings reported by {holder.Name}.");

        return (holder, reportDates, ResolveReportDate(reportDate, reportDates), null);
    }

    private IQueryable<DateOnly> GetReportDatesByHolder(InstitutionalHolder holder) =>
        _holdingRepository
            .GetHistoryByHolder(holder)
            .Select(h => h.ReportDate)
            .Distinct()
            .OrderByDescending(d => d);

    private IQueryable<DateOnly> GetReportDatesByStock(CommonStock stock) =>
        _holdingRepository
            .GetHistoryByStock(stock)
            .Select(h => h.ReportDate)
            .Distinct()
            .OrderByDescending(d => d);

    private static bool TryParseReportDate(string input, out DateOnly result)
    {
        result = default;
        return !string.IsNullOrEmpty(input) && DateOnly.TryParse(input, out result);
    }

    private static DateOnly ResolveReportDate(string input, IReadOnlyList<DateOnly> validDates) =>
        TryParseReportDate(input, out var parsed) && validDates.Contains(parsed)
            ? parsed
            : validDates[0];

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
}
