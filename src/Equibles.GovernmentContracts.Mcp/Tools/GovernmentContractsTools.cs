using System.ComponentModel;
using System.Globalization;
using System.Text;
using Equibles.CommonStocks.Repositories;
using Equibles.CommonStocks.Repositories.Extensions;
using Equibles.Core.Extensions;
using Equibles.Data;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.BusinessLogic.Extensions;
using Equibles.GovernmentContracts.Data.Extensions;
using Equibles.GovernmentContracts.Data.Models;
using Equibles.GovernmentContracts.Repositories;
using Equibles.Mcp;
using Equibles.Mcp.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace Equibles.GovernmentContracts.Mcp.Tools;

[McpServerToolType]
public class GovernmentContractsTools
{
    // Awards whose latest ingested action date lags today by more than this are a data
    // gap the caller must see: the footer states the (data-driven) max ingested date.
    private const int StaleCoverageDays = 60;

    private readonly GovernmentContractRepository _contractRepository;
    private readonly CommonStockRepository _commonStockRepository;
    private readonly McpToolRunner _runner;

    public GovernmentContractsTools(
        GovernmentContractRepository contractRepository,
        CommonStockRepository commonStockRepository,
        ErrorManager errorManager,
        ILogger<GovernmentContractsTools> logger
    )
    {
        _contractRepository = contractRepository;
        _commonStockRepository = commonStockRepository;
        _runner = new McpToolRunner(logger, errorManager.AsMcpErrorReporter());
    }

    [McpServerTool(
        Name = "GetGovernmentContracts",
        Title = "Federal Contracts by Company",
        ReadOnly = true
    )]
    [Description(
        "Get federal government contract awards (from USAspending.gov) won by a specific public company. "
            + "Shows the award (action) date, recipient named by the government, awarding agency, total value "
            + "(obligated dollars plus unexercised ceiling — not revenue received), outlays when reported, "
            + "period-of-performance end date, and description. "
            + "Coverage: only prime contract awards of $1M or more that resolve to a listed company are "
            + "included, so sums understate total federal revenue. Useful for gauging a company's reliance "
            + "on federal spending; use GetTopGovernmentContractors to rank companies market-wide."
    )]
    public Task<string> GetGovernmentContracts(
        [Description("Stock ticker symbol (e.g., LMT, RTX, BA)")] string ticker,
        [Description(
            "Start date in YYYY-MM-DD format, filtering on the award action date (defaults to 1 year ago)"
        )]
            string startDate = null,
        [Description("End date in YYYY-MM-DD format (defaults to today)")] string endDate = null,
        [Description("Maximum number of awards to return (default: 50)")] int maxResults = 50,
        [Description(
            "Sort order: 'amount' (largest total value first, default) or 'date' (most recent award first)"
        )]
            string sortBy = "amount",
        [Description(
            "Optional case-insensitive substring filter on the awarding agency (e.g., 'Defense')"
        )]
            string agency = null,
        [Description("Number of matching awards to skip before returning rows (default: 0)")]
            int offset = 0
    )
    {
        return _runner.Execute(
            async () =>
            {
                var (stock, stockError) = await _commonStockRepository.ResolveByTicker(ticker);
                if (stockError != null)
                    return stockError;

                var (start, end, rangeError) = ParseAwardDateRange(startDate, endDate);
                if (rangeError != null)
                    return rangeError;

                var (sortByDate, sortError) = ParseSortBy(sortBy);
                if (sortError != null)
                    return sortError;

                maxResults = McpLimit.Clamp(maxResults);
                offset = McpLimit.ClampOffset(offset);

                var query = _contractRepository
                    .GetByCommonStock(stock)
                    .Where(c => c.ActionDate >= start && c.ActionDate <= end);

                if (!string.IsNullOrWhiteSpace(agency))
                {
                    var pattern = LikePattern.Contains(agency.Trim());
                    query = query.Where(c =>
                        c.AwardingAgency != null
                        && EF.Functions.ILike(c.AwardingAgency, pattern, LikePattern.EscapeChar)
                    );
                }

                var totalCount = await query.CountAsync();
                var totalValue = totalCount == 0 ? 0m : await query.SumAsync(c => c.Amount);

                var awards = await query
                    .OrderForPublicSurface(sortByDate)
                    .Skip(offset)
                    .Take(maxResults)
                    .ToListAsync();
                if (awards.Count == 0 && offset > 0)
                    return $"No results at offset {offset} - only {totalCount} federal contract awards match; lower offset.";

                // USAspending reports outlays on only about a quarter of awards, so a permanent
                // column would be mostly em-dashes; it renders when this answer actually carries
                // the figure. Recipient is unconditional — it is the attribution evidence.
                var hasOutlays = awards.Any(c => c.TotalOutlays != null);

                var table = MarkdownTable.Render(
                    awards,
                    $"No federal contract awards found for {stock.Ticker} between {start:yyyy-MM-dd} and {end:yyyy-MM-dd}.",
                    $"Federal contract awards for {stock.Ticker} ({stock.Name}), {start:yyyy-MM-dd} to {end:yyyy-MM-dd} "
                        + $"— {totalCount} awards totaling {FormatUsd(totalValue)}:",
                    hasOutlays
                        ? "| Award Date | Recipient | Agency | Type | Total Value (obligated + ceiling) | Outlays | Period End | Award ID | Description |"
                        : "| Award Date | Recipient | Agency | Type | Total Value (obligated + ceiling) | Period End | Award ID | Description |",
                    hasOutlays
                        ? "|------------|-----------|--------|------|-----------------------------------|---------|------------|----------|-------------|"
                        : "|------------|-----------|--------|------|-----------------------------------|------------|----------|-------------|",
                    c =>
                        $"| {Format(c.ActionDate)} | {Escape(c.RecipientName)} | {Escape(c.AwardingAgency)} | {AwardTypeLabel(c.AwardType)} "
                        + $"| {FormatUsd(c.Amount)} "
                        + (
                            hasOutlays
                                ? $"| {(c.TotalOutlays == null ? "—" : FormatUsd(c.TotalOutlays.Value))} "
                                : ""
                        )
                        + $"| {Format(c.EndDate)} | {Escape(c.AwardId)} "
                        + $"| {Escape(Shorten(c.Description, 80))} |"
                );

                return await AppendFooters(
                    table,
                    awards.Count,
                    totalCount,
                    end,
                    hasOutlays,
                    hasRecipient: true,
                    offset: offset
                );
            },
            "GetGovernmentContracts",
            $"ticker: {ticker}, agency: {agency}, sortBy: {sortBy}, offset: {offset}"
        );
    }

    [McpServerTool(
        Name = "GetTopGovernmentContractors",
        Title = "Top Federal Contractors",
        ReadOnly = true
    )]
    [Description(
        "Rank public companies by total federal contract dollars awarded over a date range (from "
            + "USAspending.gov). Sums the total award value (obligated dollars plus unexercised ceiling) of "
            + "prime contract awards of $1M or more that resolve to a listed company; smaller awards and "
            + "unlisted recipients are excluded. Answers questions like 'which public companies won the most "
            + "federal contracts last quarter'. Use GetGovernmentContracts for one company's individual awards."
    )]
    public Task<string> GetTopGovernmentContractors(
        [Description(
            "Start date in YYYY-MM-DD format, filtering on the award action date (defaults to 1 year ago)"
        )]
            string startDate = null,
        [Description("End date in YYYY-MM-DD format (defaults to today)")] string endDate = null,
        [Description("Maximum number of companies to return (default: 25, largest first)")]
            int maxResults = 25
    )
    {
        return _runner.Execute(
            async () =>
            {
                var (start, end, rangeError) = ParseAwardDateRange(startDate, endDate);
                if (rangeError != null)
                    return rangeError;

                maxResults = McpLimit.Clamp(maxResults);

                var window = _contractRepository
                    .GetAll()
                    .Where(c => c.ActionDate >= start && c.ActionDate <= end);

                var totalCompanies = await window
                    .Select(c => c.CommonStockId)
                    .Distinct()
                    .CountAsync();

                var ranked = await window
                    .GroupBy(c => new
                    {
                        c.CommonStockId,
                        c.CommonStock.Ticker,
                        c.CommonStock.Name,
                    })
                    .Select(g => new
                    {
                        g.Key.Ticker,
                        g.Key.Name,
                        Total = g.Sum(c => c.Amount),
                        Count = g.Count(),
                    })
                    .OrderByDescending(r => r.Total)
                    .Take(maxResults)
                    .ToListAsync();

                var rank = 0;
                var table = MarkdownTable.Render(
                    ranked,
                    $"No federal contract awards found between {start:yyyy-MM-dd} and {end:yyyy-MM-dd}.",
                    $"Top federal contractors ({start:yyyy-MM-dd} to {end:yyyy-MM-dd}):",
                    "| Rank | Ticker | Company | Total Value (obligated + ceiling) | Awards |",
                    "|------|--------|---------|-----------------------------------|--------|",
                    r =>
                        $"| {++rank} | {Escape(r.Ticker)} | {Escape(r.Name)} | {FormatUsd(r.Total)} | {r.Count} |"
                );

                return await AppendFooters(
                    table,
                    ranked.Count,
                    totalCompanies,
                    end,
                    hasOutlays: false,
                    hasRecipient: false
                );
            },
            "GetTopGovernmentContractors",
            $"range: {startDate}..{endDate}"
        );
    }

    // Every result carries the dataset's coverage limits and its true recency, so a sparse
    // window reads as a data gap rather than as an absence of awards.
    private async Task<string> AppendFooters(
        string table,
        int shown,
        int total,
        DateOnly rangeEnd,
        bool hasOutlays,
        bool hasRecipient,
        int? offset = null
    )
    {
        var sb = new StringBuilder(table.TrimEnd('\n', '\r'));
        sb.AppendLine();
        sb.AppendLine();

        var truncationNote = offset.HasValue
            ? McpOutput.PagedTruncationNote(shown, total, offset.Value)
            : McpOutput.TruncationNote(shown, total);
        if (truncationNote.Length > 0)
            sb.AppendLine(truncationNote);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        // Future action dates are upstream glitches, not evidence of coverage — the
        // freshness statement keys on the newest credible (non-future) action date.
        var latestIngested = await _contractRepository
            .GetAll()
            .Where(c => c.ActionDate != null && c.ActionDate <= today)
            .MaxAsync(c => c.ActionDate);

        sb.AppendLine(
            "_Coverage: prime federal contract awards of $1M+ matched to listed companies; "
                + "Total Value is obligated dollars plus unexercised option ceiling, not revenue received"
                + (
                    hasOutlays
                        ? "; Outlays is what the government has actually disbursed so far"
                        : ""
                )
                + (
                    hasRecipient
                        ? ". Recipient is the awarded entity as the government named it — a subsidiary's own name there is how an award reaches this company. "
                        : ". "
                )
                + $"Latest ingested award action date: {Format(latestIngested)}._"
        );

        if (latestIngested == null)
        {
            sb.AppendLine("_Data coverage warning: no award data has been ingested yet._");
        }
        else if (latestIngested < today.AddDays(-StaleCoverageDays) && rangeEnd > latestIngested)
        {
            sb.AppendLine(
                $"_Data coverage warning: no awards have been ingested after {Format(latestIngested)}; "
                    + "results after that date are incomplete._"
            );
        }

        return sb.ToString();
    }

    // Strict date arguments: a malformed date must correct the caller, never silently fall
    // back to the default window — the caller could not tell which range was actually applied.
    private static (DateOnly Date, string Error) ParseDateArgument(
        string value,
        string argumentName,
        DateOnly fallback
    )
    {
        if (string.IsNullOrWhiteSpace(value))
            return (fallback, null);
        if (McpOutput.TryParseDate(value, out var parsed))
            return (DateOnly.FromDateTime(parsed), null);
        return (default, McpOutput.InvalidArgument(argumentName, value, "yyyy-MM-dd"));
    }

    private static (DateOnly Start, DateOnly End, string Error) ParseAwardDateRange(
        string startDate,
        string endDate
    )
    {
        var (start, startError) = ParseDateArgument(
            startDate,
            "startDate",
            McpToolExecutor.UtcYearsAgo(1)
        );
        if (startError != null)
            return (default, default, startError);

        var (end, endError) = ParseDateArgument(
            endDate,
            "endDate",
            DateOnly.FromDateTime(DateTime.UtcNow)
        );
        if (endError != null)
            return (default, default, endError);

        // An inverted range must error, never return the generic empty-result message —
        // the caller would misread it as "the company won no awards".
        if (start > end)
            return (
                default,
                default,
                $"Invalid date range: startDate {start:yyyy-MM-dd} is after endDate {end:yyyy-MM-dd}."
            );

        return (start, end, null);
    }

    // An unrecognised sort must error, never silently fall back to amount ordering: a
    // caller asking for recent awards would misread the largest-first rows as the newest.
    private static (bool ByDate, string Error) ParseSortBy(string sortBy)
    {
        if (string.IsNullOrWhiteSpace(sortBy))
            return (false, null);

        return sortBy.Trim().ToLowerInvariant() switch
        {
            "amount" => (false, null),
            "date" => (true, null),
            _ => (
                false,
                McpOutput.InvalidArgument(
                    "sortBy",
                    sortBy,
                    "'amount' (largest first) or 'date' (most recent first)"
                )
            ),
        };
    }

    private static string AwardTypeLabel(GovernmentContractAwardType type) =>
        type switch
        {
            GovernmentContractAwardType.BpaCall => "BPA Call",
            GovernmentContractAwardType.PurchaseOrder => "Purchase Order",
            GovernmentContractAwardType.DeliveryOrder => "Delivery Order",
            GovernmentContractAwardType.DefinitiveContract => "Definitive Contract",
            _ => "Unknown",
        };

    private static string FormatUsd(decimal amount) =>
        "$" + amount.ToString("N0", CultureInfo.InvariantCulture);

    private static string Format(DateOnly? date) =>
        date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "—";

    private static string Shorten(string value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var trimmed = value.Trim();
        if (trimmed.Length <= maxLength)
            return trimmed;
        return trimmed.TruncateToFit(maxLength) + "…";
    }

    private static string Escape(string value) => MarkdownTable.EscapeCell(value);
}
