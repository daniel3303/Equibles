using System.ComponentModel;
using System.Text;
using Equibles.CommonStocks.Repositories;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Repositories;
using Equibles.Mcp;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace Equibles.Holdings.Mcp.Tools;

[McpServerToolType]
public class InstitutionalHoldingsTools
{
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
        return McpToolExecutor.Execute(
            async () =>
            {
                var stock = await _commonStockRepository.GetByTicker(ticker);
                if (stock == null)
                    return $"Stock '{ticker}' not found.";

                DateOnly targetDate;
                if (
                    !string.IsNullOrEmpty(reportDate)
                    && DateOnly.TryParse(reportDate, out var parsed)
                )
                {
                    targetDate = parsed;
                }
                else
                {
                    var latestDate = await _holdingRepository
                        .GetHistoryByStock(stock)
                        .Select(h => h.ReportDate)
                        .Distinct()
                        .OrderByDescending(d => d)
                        .FirstOrDefaultAsync();

                    if (latestDate == default)
                        return $"No institutional holdings data available for {ticker}.";
                    targetDate = latestDate;
                }

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
            },
            _logger,
            "GetTopHolders",
            $"ticker: {ticker}",
            ReportError
        );
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
        return McpToolExecutor.Execute(
            async () =>
            {
                var stock = await _commonStockRepository.GetByTicker(ticker);
                if (stock == null)
                    return $"Stock '{ticker}' not found.";

                var reportDates = await _holdingRepository
                    .GetHistoryByStock(stock)
                    .Select(h => h.ReportDate)
                    .Distinct()
                    .OrderByDescending(d => d)
                    .Take(maxPeriods)
                    .ToListAsync();

                if (reportDates.Count == 0)
                    return $"No institutional holdings history available for {ticker}.";

                var result = new StringBuilder();
                result.AppendLine($"Institutional ownership history for {stock.Name} ({ticker}):");
                result.AppendLine();
                result.AppendLine(
                    "| Report Date | Institutions | Total Shares | Total Value ($M) | Change |"
                );
                result.AppendLine(
                    "|------------|-------------|-------------|-----------------|--------|"
                );

                long previousShares = 0;
                foreach (var date in reportDates.OrderBy(d => d))
                {
                    var holdings = await _holdingRepository.GetByStock(stock, date).ToListAsync();
                    var totalShares = holdings.Sum(h => h.Shares);
                    var totalValue = holdings.Sum(h => h.Value);
                    var institutionCount = holdings
                        .Select(h => h.InstitutionalHolderId)
                        .Distinct()
                        .Count();

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
            },
            _logger,
            "GetOwnershipHistory",
            $"ticker: {ticker}",
            ReportError
        );
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
        return McpToolExecutor.Execute(
            async () =>
            {
                var holders = await _holderRepository.Search(institutionName).Take(5).ToListAsync();

                if (holders.Count == 0)
                    return $"No institution found matching '{institutionName}'.";

                var holder = holders.First();

                DateOnly targetDate;
                if (
                    !string.IsNullOrEmpty(reportDate)
                    && DateOnly.TryParse(reportDate, out var parsed)
                )
                {
                    targetDate = parsed;
                }
                else
                {
                    var latestDate = await _holdingRepository
                        .GetHistoryByHolder(holder)
                        .Select(h => h.ReportDate)
                        .Distinct()
                        .OrderByDescending(d => d)
                        .FirstOrDefaultAsync();

                    if (latestDate == default)
                        return $"No holdings data for {holder.Name}.";
                    targetDate = latestDate;
                }

                var holdings = await _holdingRepository
                    .GetByHolder(holder, targetDate)
                    .OrderByDescending(h => h.Value)
                    .Take(maxResults)
                    .ToListAsync();

                if (holdings.Count == 0)
                    return $"No holdings found for {holder.Name} as of {targetDate:yyyy-MM-dd}.";

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
            },
            _logger,
            "GetInstitutionPortfolio",
            $"institution: {institutionName}",
            ReportError
        );
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
        return McpToolExecutor.Execute(
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
            _logger,
            "SearchInstitutions",
            $"query: {query}",
            ReportError
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
        return McpToolExecutor.Execute(
            async () =>
            {
                var stock = await _commonStockRepository.GetByTicker(ticker);
                if (stock == null)
                    return $"Stock '{ticker}' not found.";

                var reportDates = await _holdingRepository
                    .GetHistoryByStock(stock)
                    .Select(h => h.ReportDate)
                    .Distinct()
                    .OrderByDescending(d => d)
                    .ToListAsync();

                var targetDate =
                    !string.IsNullOrEmpty(reportDate)
                    && DateOnly.TryParse(reportDate, out var parsed)
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
                        return new
                        {
                            Name = c?.Name ?? p?.Name ?? "Unknown",
                            CurrentShares = c?.Shares ?? 0,
                            PreviousShares = p?.Shares ?? 0,
                            DeltaShares = (c?.Shares ?? 0) - (p?.Shares ?? 0),
                            DeltaValue = (c?.Value ?? 0) - (p?.Value ?? 0),
                        };
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

                var result = new StringBuilder();
                result.AppendLine(
                    $"Top buyers and sellers of {stock.Name} ({ticker}) as of {targetDate:yyyy-MM-dd}"
                );
                if (previousDate.HasValue)
                    result.AppendLine($"vs prior quarter {previousDate.Value:yyyy-MM-dd}");
                result.AppendLine();

                result.AppendLine("## Top Buyers");
                if (topBuyers.Count == 0)
                {
                    result.AppendLine("_No buyers this quarter._");
                }
                else
                {
                    result.AppendLine(
                        "| # | Institution | Δ Shares | Δ Value ($M) | Prior → New Shares |"
                    );
                    result.AppendLine(
                        "|---|-------------|---------|-------------|------------------|"
                    );
                    for (var i = 0; i < topBuyers.Count; i++)
                    {
                        var m = topBuyers[i];
                        result.AppendLine(
                            $"| {i + 1} | {m.Name} | +{m.DeltaShares:N0} | {m.DeltaValue / 1_000_000m:+#,##0.0;-#,##0.0;0.0} | {m.PreviousShares:N0} → {m.CurrentShares:N0} |"
                        );
                    }
                }

                result.AppendLine();
                result.AppendLine("## Top Sellers");
                if (topSellers.Count == 0)
                {
                    result.AppendLine("_No sellers this quarter._");
                }
                else
                {
                    result.AppendLine(
                        "| # | Institution | Δ Shares | Δ Value ($M) | Prior → New Shares |"
                    );
                    result.AppendLine(
                        "|---|-------------|---------|-------------|------------------|"
                    );
                    for (var i = 0; i < topSellers.Count; i++)
                    {
                        var m = topSellers[i];
                        result.AppendLine(
                            $"| {i + 1} | {m.Name} | {m.DeltaShares:N0} | {m.DeltaValue / 1_000_000m:+#,##0.0;-#,##0.0;0.0} | {m.PreviousShares:N0} → {m.CurrentShares:N0} |"
                        );
                    }
                }

                return result.ToString();
            },
            _logger,
            "GetTopBuyersSellers",
            $"ticker: {ticker}",
            ReportError
        );
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
        return McpToolExecutor.Execute(
            async () =>
            {
                var normalizedBucket = (bucket ?? string.Empty).Trim().ToLowerInvariant();
                if (
                    normalizedBucket != "top-buys"
                    && normalizedBucket != "top-sells"
                    && normalizedBucket != "new-positions"
                    && normalizedBucket != "sold-out-positions"
                )
                    return "Unknown bucket. Use one of: top-buys, top-sells, new-positions, sold-out-positions.";

                var reportDates = await _holdingRepository
                    .GetAvailableReportDates()
                    .Distinct()
                    .OrderByDescending(d => d)
                    .ToListAsync();
                if (reportDates.Count == 0)
                    return "No 13F holdings data available.";

                DateOnly targetDate;
                if (
                    !string.IsNullOrEmpty(reportDate)
                    && DateOnly.TryParse(reportDate, out var parsed)
                )
                    targetDate = parsed;
                else
                    targetDate = reportDates[0];
                var targetIndex = reportDates.IndexOf(targetDate);
                if (targetIndex < 0)
                    return $"Report date {targetDate:yyyy-MM-dd} not found. Available dates: {string.Join(", ", reportDates.Take(5).Select(d => d.ToString("yyyy-MM-dd")))}{(reportDates.Count > 5 ? "…" : "")}.";

                var previousDate =
                    targetIndex < reportDates.Count - 1
                        ? reportDates[targetIndex + 1]
                        : (DateOnly?)null;
                if (!previousDate.HasValue)
                    return $"No prior quarter to compare against for {targetDate:yyyy-MM-dd}.";

                // Headline + comparison subtitle.
                var result = new StringBuilder();
                result.AppendLine(
                    $"Market-wide 13F **{normalizedBucket}** for {targetDate:yyyy-MM-dd}"
                );
                result.AppendLine($"vs prior quarter {previousDate.Value:yyyy-MM-dd}");
                result.AppendLine();

                if (normalizedBucket is "top-buys" or "top-sells")
                {
                    var activity = _holdingRepository.GetQuarterlyActivity(
                        targetDate,
                        previousDate.Value
                    );
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
                        return result.ToString()
                            + "_No stocks moved in this direction this quarter._";

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
                else
                {
                    var churn = _holdingRepository.GetQuarterlyNewSoldOutPositions(
                        targetDate,
                        previousDate.Value
                    );
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
                        return result.ToString() + "_No stocks in this bucket this quarter._";

                    var stockIds = rows.Select(r => r.CommonStockId).ToList();
                    var stocks = await _commonStockRepository
                        .GetAll()
                        .Where(s => stockIds.Contains(s.Id))
                        .ToDictionaryAsync(s => s.Id);

                    var label =
                        normalizedBucket == "new-positions"
                            ? "# Filers Initiated"
                            : "# Filers Exited";
                    result.AppendLine($"| # | Ticker | Company | {label} |");
                    result.AppendLine("|---|--------|---------|-------------|");
                    for (var i = 0; i < rows.Count; i++)
                    {
                        var r = rows[i];
                        stocks.TryGetValue(r.CommonStockId, out var s);
                        var count =
                            normalizedBucket == "new-positions"
                                ? r.NewFilerCount
                                : r.SoldOutFilerCount;
                        result.AppendLine(
                            $"| {i + 1} | {s?.Ticker ?? "—"} | {s?.Name ?? "Unknown"} | {count:N0} |"
                        );
                    }
                    return result.ToString();
                }
            },
            _logger,
            "GetMarketWide13FActivity",
            $"bucket: {bucket}",
            ReportError
        );
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
        return McpToolExecutor.Execute(
            async () =>
            {
                var holder = await _holderRepository
                    .Search(institutionName ?? string.Empty)
                    .OrderBy(h => h.Name)
                    .FirstOrDefaultAsync();
                if (holder == null)
                    return $"No institution found matching '{institutionName}'.";

                var reportDates = await _holdingRepository
                    .GetHistoryByHolder(holder)
                    .Select(h => h.ReportDate)
                    .Distinct()
                    .OrderByDescending(d => d)
                    .ToListAsync();
                if (reportDates.Count == 0)
                    return $"No 13F holdings reported by {holder.Name}.";

                DateOnly targetDate;
                if (
                    !string.IsNullOrEmpty(reportDate)
                    && DateOnly.TryParse(reportDate, out var parsed)
                    && reportDates.Contains(parsed)
                )
                    targetDate = parsed;
                else
                    targetDate = reportDates[0];
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

                var result = new StringBuilder();
                result.AppendLine(
                    $"Portfolio summary — **{holder.Name}** as of {targetDate:yyyy-MM-dd}"
                );
                if (previousDate.HasValue)
                    result.AppendLine($"vs prior quarter {previousDate.Value:yyyy-MM-dd}");
                result.AppendLine();
                result.AppendLine("| Metric | Value |");
                result.AppendLine("|--------|-------|");
                result.AppendLine($"| Reported AUM | ${summary.ReportedAum:N0} |");
                result.AppendLine($"| # Positions | {summary.PositionCount:N0} |");
                result.AppendLine(
                    $"| Top 10 concentration | {summary.Top10ConcentrationPercent:F1}% |"
                );
                result.AppendLine(
                    $"| Top 25 concentration | {summary.Top25ConcentrationPercent:F1}% |"
                );
                result.AppendLine($"| QoQ turnover | {summary.QoQTurnoverPercent:F1}% |");
                result.AppendLine($"| Quarters reported | {summary.QuartersReported} |");
                result.AppendLine();
                result.AppendLine(
                    "_QoQ turnover = (Σ |Δ shares × current price proxy|) / (2 × AUM), where the per-share price proxy is the current quarter's Value / Shares._"
                );

                return result.ToString();
            },
            _logger,
            "GetInstitutionSummary",
            $"institution: {institutionName}",
            ReportError
        );
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
        return McpToolExecutor.Execute(
            async () =>
            {
                var holder = await _holderRepository
                    .Search(institutionName ?? string.Empty)
                    .OrderBy(h => h.Name)
                    .FirstOrDefaultAsync();
                if (holder == null)
                    return $"No institution found matching '{institutionName}'.";

                var reportDates = await _holdingRepository
                    .GetHistoryByHolder(holder)
                    .Select(h => h.ReportDate)
                    .Distinct()
                    .OrderByDescending(d => d)
                    .ToListAsync();
                if (reportDates.Count == 0)
                    return $"No 13F holdings reported by {holder.Name}.";

                DateOnly targetDate;
                if (
                    !string.IsNullOrEmpty(reportDate)
                    && DateOnly.TryParse(reportDate, out var parsed)
                    && reportDates.Contains(parsed)
                )
                    targetDate = parsed;
                else
                    targetDate = reportDates[0];

                var holdings = await _holdingRepository
                    .GetByHolder(holder, targetDate)
                    .Include(h => h.CommonStock)
                        .ThenInclude(s => s.Industry)
                    .ToListAsync();
                var slices = IndustryAllocationCalculator.Calculate(holdings);

                var result = new StringBuilder();
                result.AppendLine(
                    $"Sector allocation — **{holder.Name}** as of {targetDate:yyyy-MM-dd}"
                );
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
            },
            _logger,
            "GetInstitutionSectorAllocation",
            $"institution: {institutionName}",
            ReportError
        );
    }

    private Task ReportError(string toolName, string message, string stackTrace, string context)
    {
        return _errorManager.Create(ErrorSource.McpTool, toolName, message, stackTrace, context);
    }

    private class HolderAggregate
    {
        public string Name { get; set; }
        public long Shares { get; set; }
        public long Value { get; set; }
    }
}
