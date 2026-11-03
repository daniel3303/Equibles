using System.ComponentModel;
using System.Globalization;
using System.Text;
using Equibles.CommonStocks.Data.Helpers;
using Equibles.CommonStocks.Data.Models;
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
    private readonly EquityIssuerRepository _commonStockRepository;
    private readonly McpToolRunner _runner;

    public GovernmentContractsTools(
        GovernmentContractRepository contractRepository,
        EquityIssuerRepository commonStockRepository,
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
        "Get federal government contract awards won by a specific public company: US federal awards "
            + "(from USAspending.gov) or Government of Canada contracts (from its proactive publication of "
            + "contracts over C$10,000). Shows the award (action) date, recipient named by the government, "
            + "awarding agency or department, total value (US: obligated dollars plus unexercised ceiling; "
            + "Canada: contract value including amendments, in Canadian dollars; never revenue received), "
            + "outlays when reported, end date, and description. "
            + "Coverage: US prime contract awards of $1M or more, and Canadian contracts reported from fiscal "
            + "2017-2018 (April 2017) whose vendor name exactly matches a company listed on a Canadian exchange, "
            + "so sums understate government revenue. "
            + "Use GetTopGovernmentContractors to rank companies market-wide."
    )]
    public Task<string> GetGovernmentContracts(
        [Description(
            "Stock ticker symbol (e.g., LMT, RTX, BA), or MIC:TICKER for a Canadian listing (e.g., XTSE:CAE)"
        )]
            string ticker,
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
            int offset = 0,
        [Description(
            "Awarding government: US or CA. Defaults to the country of the listing the symbol names (CA for XTSE:CAE, US for a bare US ticker)"
        )]
            string country = null
    )
    {
        return _runner.Execute(
            async () =>
            {
                var (stock, venue, stockError) = EquityListingSymbol.TryParse(ticker, out _, out _)
                    ? await _commonStockRepository.ResolveListing(ticker)
                    : await ResolveIssuer(ticker);
                if (stockError != null)
                    return stockError;

                var (countryCode, countryError) = ParseCountry(
                    country,
                    venue?.MarketCountryCode ?? stock.Presentation?.Listing.MarketCountryCode
                );
                if (countryError != null)
                    return countryError;

                var (start, end, rangeError) = ParseAwardDateRange(startDate, endDate);
                if (rangeError != null)
                    return rangeError;

                var (sortByDate, sortError) = ParseSortBy(sortBy);
                if (sortError != null)
                    return sortError;

                maxResults = McpLimit.Clamp(maxResults);
                offset = McpLimit.ClampOffset(offset);

                var query = _contractRepository
                    .GetByIssuerId(stock.Id, countryCode)
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
                var noun = AwardNoun(countryCode);
                if (awards.Count == 0 && offset > 0)
                    return $"No results at offset {offset} - only {totalCount} {noun} match; lower offset.";

                // USAspending reports outlays on only about a quarter of awards, so a permanent
                // column would be mostly em-dashes; it renders when this answer actually carries
                // the figure. Recipient is unconditional — it is the attribution evidence.
                var hasOutlays = awards.Any(c => c.TotalOutlays != null);

                var table = MarkdownTable.Render(
                    awards,
                    $"No {noun} found for {Symbol(stock, venue)} between {start:yyyy-MM-dd} and {end:yyyy-MM-dd}.",
                    $"{char.ToUpperInvariant(noun[0])}{noun[1..]} for {Symbol(stock, venue)} ({stock.Name}), {start:yyyy-MM-dd} to {end:yyyy-MM-dd} "
                        + $"— {totalCount} awards totaling {FormatAmount(totalValue, countryCode)}:",
                    hasOutlays
                        ? $"| Award Date | Recipient | Agency | Type | {ValueHeader(countryCode)} | Outlays | Period End | Award ID | Description |"
                        : $"| Award Date | Recipient | Agency | Type | {ValueHeader(countryCode)} | Period End | Award ID | Description |",
                    hasOutlays
                        ? "|------------|-----------|--------|------|-----------------------------------|---------|------------|----------|-------------|"
                        : "|------------|-----------|--------|------|-----------------------------------|------------|----------|-------------|",
                    c =>
                        $"| {Format(c.ActionDate)} | {Escape(c.RecipientName)} | {Escape(c.AwardingAgency)} | {AwardTypeLabel(c.AwardType)} "
                        + $"| {FormatAmount(c.Amount, countryCode)} "
                        + (
                            hasOutlays
                                ? $"| {(c.TotalOutlays == null ? "—" : FormatAmount(c.TotalOutlays.Value, countryCode))} "
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
                    countryCode,
                    offset: offset
                );
            },
            "GetGovernmentContracts",
            $"ticker: {ticker}, agency: {agency}, sortBy: {sortBy}, offset: {offset}, country: {country}"
        );
    }

    [McpServerTool(
        Name = "GetTopGovernmentContractors",
        Title = "Top Federal Contractors",
        ReadOnly = true
    )]
    [Description(
        "Rank public companies by total federal contract dollars awarded over a date range, one country "
            + "at a time: US (default, from USAspending.gov; total award value of prime contract awards of "
            + "$1M or more) or CA (Government of Canada contracts over C$10,000, contract value including "
            + "amendments, in Canadian dollars, for vendors whose name exactly matches a company listed on a "
            + "Canadian exchange). Unlisted recipients are excluded. Answers questions like 'which public "
            + "companies won the most federal contracts last quarter'. Use GetGovernmentContracts for one "
            + "company's individual awards."
    )]
    public Task<string> GetTopGovernmentContractors(
        [Description(
            "Start date in YYYY-MM-DD format, filtering on the award action date (defaults to 1 year ago)"
        )]
            string startDate = null,
        [Description("End date in YYYY-MM-DD format (defaults to today)")] string endDate = null,
        [Description("Maximum number of companies to return (default: 25, largest first)")]
            int maxResults = 25,
        [Description("Awarding government: US (default) or CA")] string country = "US"
    )
    {
        return _runner.Execute(
            async () =>
            {
                var (start, end, rangeError) = ParseAwardDateRange(startDate, endDate);
                if (rangeError != null)
                    return rangeError;

                var (countryCode, countryError) = ParseCountry(country, "US");
                if (countryError != null)
                    return countryError;

                maxResults = McpLimit.Clamp(maxResults);

                var window = _contractRepository
                    .GetByCountry(countryCode)
                    .Where(c => c.ActionDate >= start && c.ActionDate <= end);

                var totalCompanies = await window
                    .Select(c => c.EquityIssuerId)
                    .Distinct()
                    .CountAsync();

                var ranked = await window
                    .GroupBy(c => new
                    {
                        c.EquityIssuerId,
                        Ticker = c.Issuer.Presentation == null
                            ? null
                            : c.Issuer.Presentation.Listing.Ticker,
                        Country = c.Issuer.Presentation == null
                            ? null
                            : c.Issuer.Presentation.Listing.MarketCountryCode,
                        Market = c.Issuer.Presentation == null
                            ? null
                            : c.Issuer.Presentation.Listing.MarketIdentifierCode,
                        c.Issuer.Name,
                    })
                    .Select(g => new
                    {
                        g.Key.Ticker,
                        g.Key.Country,
                        g.Key.Market,
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
                    $"No {AwardNoun(countryCode)} found between {start:yyyy-MM-dd} and {end:yyyy-MM-dd}.",
                    countryCode == "CA"
                        ? $"Top Government of Canada contractors ({start:yyyy-MM-dd} to {end:yyyy-MM-dd}):"
                        : $"Top federal contractors ({start:yyyy-MM-dd} to {end:yyyy-MM-dd}):",
                    $"| Rank | Ticker | Company | {ValueHeader(countryCode)} | Awards |",
                    "|------|--------|---------|-----------------------------------|--------|",
                    r =>
                        // A company listed only outside the US is named by MIC:TICKER, since its bare ticker can be another US company's.
                        $"| {++rank} | {Escape(EquityListingSymbol.Display(r.Ticker, r.Country, r.Market))} | {Escape(r.Name)} | {FormatAmount(r.Total, countryCode)} | {r.Count} |"
                );

                return await AppendFooters(
                    table,
                    ranked.Count,
                    totalCompanies,
                    end,
                    hasOutlays: false,
                    hasRecipient: false,
                    countryCode
                );
            },
            "GetTopGovernmentContractors",
            $"range: {startDate}..{endDate}, country: {country}"
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
        string countryCode,
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
            .GetByCountry(countryCode)
            .Where(c => c.ActionDate != null && c.ActionDate <= today)
            .MaxAsync(c => c.ActionDate);

        sb.AppendLine(
            (
                countryCode == "CA"
                    ? "_Coverage: Government of Canada contracts over C$10,000 reported from fiscal 2017-2018 (April 2017) whose "
                        + "vendor name exactly matches a company listed on a Canadian exchange (a subsidiary named differently is not "
                        + "matched); Contract Value is the "
                        + "latest reported value including amendments, in Canadian dollars, not revenue received"
                    : "_Coverage: prime federal contract awards of $1M+ matched to listed companies; "
                        + "Total Value is obligated dollars plus unexercised option ceiling, not revenue received"
            )
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
            GovernmentContractAwardType.Contract => "Contract",
            GovernmentContractAwardType.StandingOfferCallUp => "Standing Offer Call-up",
            _ => "Unknown",
        };

    // A Canadian amount carries its currency, since the same issuer can hold US awards in US dollars.
    private static string FormatAmount(decimal amount, string countryCode) =>
        (countryCode == "CA" ? "C$" : "$") + amount.ToString("N0", CultureInfo.InvariantCulture);

    private static string ValueHeader(string countryCode) =>
        countryCode == "CA"
            ? "Contract Value (C$, incl. amendments)"
            : "Total Value (obligated + ceiling)";

    private static string AwardNoun(string countryCode) =>
        countryCode == "CA" ? "Government of Canada contracts" : "federal contract awards";

    private static string Symbol(EquityIssuer stock, EquityListing venue) =>
        venue != null
            ? $"{venue.MarketIdentifierCode}:{venue.Ticker}"
            : stock.Presentation?.Listing.Ticker;

    // An explicit country wins; otherwise the listing's own country decides, and anything but Canada reads US awards.
    private static (string Code, string Error) ParseCountry(string country, string listingCountry)
    {
        if (string.IsNullOrWhiteSpace(country))
            return (listingCountry == "CA" ? "CA" : "US", null);
        var code = country.Trim().ToUpperInvariant();
        return code is "US" or "CA"
            ? (code, null)
            : (null, McpOutput.InvalidArgument("country", country, "US or CA"));
    }

    private async Task<(EquityIssuer Stock, EquityListing Venue, string Error)> ResolveIssuer(
        string ticker
    )
    {
        var (stock, error) = await _commonStockRepository.ResolveIssuerIncludingDelisted(ticker);
        return (stock, null, error);
    }

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
