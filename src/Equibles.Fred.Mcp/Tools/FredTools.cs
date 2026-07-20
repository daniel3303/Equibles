using System.ComponentModel;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.BusinessLogic.Extensions;
using Equibles.Errors.Data.Models;
using Equibles.Fred.Data.Models;
using Equibles.Fred.Repositories;
using Equibles.Mcp;
using Equibles.Mcp.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace Equibles.Fred.Mcp.Tools;

[McpServerToolType]
public class FredTools
{
    private readonly FredSeriesRepository _seriesRepository;
    private readonly FredObservationRepository _observationRepository;
    private readonly FredReleaseDateRepository _releaseDateRepository;
    private readonly McpToolRunner _runner;

    public FredTools(
        FredSeriesRepository seriesRepository,
        FredObservationRepository observationRepository,
        FredReleaseDateRepository releaseDateRepository,
        ErrorManager errorManager,
        ILogger<FredTools> logger
    )
    {
        _seriesRepository = seriesRepository;
        _observationRepository = observationRepository;
        _releaseDateRepository = releaseDateRepository;
        _runner = new McpToolRunner(logger, errorManager.AsMcpErrorReporter());
    }

    [McpServerTool(
        Name = "GetEconomicIndicator",
        Title = "Economic Indicator History",
        ReadOnly = true
    )]
    [Description(
        "Get time series data for a FRED economic indicator. Returns historical observations for indicators like FEDFUNDS (fed funds rate), CPIAUCSL (CPI inflation), UNRATE (unemployment), GDP, T10Y2Y (yield spread), VIXCLS (VIX), SP500, MORTGAGE30US, M2SL (money supply), and more. Covers the curated ~40-series set Equibles tracks, not the full FRED catalog — use SearchEconomicIndicators to find available series."
    )]
    public Task<string> GetEconomicIndicator(
        [Description("FRED series ID (e.g., FEDFUNDS, CPIAUCSL, UNRATE, GDP, T10Y2Y, VIXCLS)")]
            string seriesId,
        [Description("Start date in YYYY-MM-DD format (defaults to 1 year before the end date)")]
            string startDate = null,
        [Description("End date in YYYY-MM-DD format (defaults to latest available)")]
            string endDate = null,
        [Description(
            "Maximum number of observations to return (default: 100, max: 500). When the range holds more, the newest maxResults are kept; rows are always listed in ascending date order."
        )]
            int maxResults = 100
    )
    {
        return _runner.Execute(
            async () =>
            {
                var series = await _seriesRepository
                    .GetBySeriesId(seriesId.ToUpper())
                    .FirstOrDefaultAsync();

                if (series == null)
                    return $"Series '{seriesId}' not found. Use SearchEconomicIndicators to find available series.";

                var end = DateOnly.FromDateTime(DateTime.UtcNow);
                if (!string.IsNullOrWhiteSpace(endDate))
                {
                    if (!McpOutput.TryParseDate(endDate, out var parsedEnd))
                        return McpOutput.InvalidArgument("endDate", endDate, "yyyy-MM-dd");
                    end = DateOnly.FromDateTime(parsedEnd);
                }

                // Default the start RELATIVE to the (possibly historical) end date, so
                // "endDate=2020-12-31" alone means the year up to then — not an empty
                // range against a start defaulted off today.
                var start = end.AddYears(-1);
                if (!string.IsNullOrWhiteSpace(startDate))
                {
                    if (!McpOutput.TryParseDate(startDate, out var parsedStart))
                        return McpOutput.InvalidArgument("startDate", startDate, "yyyy-MM-dd");
                    start = DateOnly.FromDateTime(parsedStart);
                }

                if (start > end)
                    return $"startDate ({start:yyyy-MM-dd}) is after endDate ({end:yyyy-MM-dd}). Swap or correct the dates.";

                maxResults = McpLimit.Clamp(maxResults);

                var rangeQuery = _observationRepository.GetBySeries(series, start, end);
                var total = await rangeQuery.CountAsync();
                var observations = await rangeQuery
                    .OrderByDescending(o => o.Date)
                    .Take(maxResults)
                    .ToListAsync();

                var table = MarkdownTable.Render(
                    observations.OrderBy(o => o.Date).ToList(),
                    $"No observations found for {series.SeriesId} ({series.Title}) in the specified date range.",
                    $"{series.Title} ({series.SeriesId})",
                    $"Units: {series.Units} | Frequency: {series.Frequency} | Seasonal Adj: {series.SeasonalAdjustment}",
                    "| Date | Value |",
                    "|------|-------|",
                    obs =>
                    {
                        // Format with InvariantCulture so the MCP markdown does not fork the
                        // decimal separator by host locale (e.g. de-DE would render 5,25).
                        var valueStr = obs.Value.HasValue
                            ? McpFormat.Invariant(obs.Value.Value, "G")
                            : "N/A";
                        return $"| {obs.Date:yyyy-MM-dd} | {valueStr} |";
                    }
                );

                // The kept rows are the NEWEST maxResults of the range (rendered
                // ascending), so the shared "first N" truncation note would mislabel
                // them — spell the newest-kept semantics out instead. Without a note a
                // long range over a daily series silently loses its start and reads as
                // if the older data does not exist.
                if (observations.Count < total)
                {
                    table +=
                        Environment.NewLine
                        + $"_Showing the newest {observations.Count} of {total} observations in the range - raise maxResults (max {McpLimit.MaxResults}) or narrow the date range for earlier data._"
                        + Environment.NewLine;
                }

                return table;
            },
            "GetEconomicIndicator",
            $"seriesId: {seriesId}"
        );
    }

    [McpServerTool(
        Name = "GetLatestEconomicData",
        Title = "Latest Economic Indicators",
        ReadOnly = true
    )]
    [Description(
        "Get the latest values for key economic indicators across categories: interest rates, yield spreads, inflation, employment, GDP, money supply, sentiment, housing, exchange rates, and market indicators. Each row shows a series' latest stored observation with its date, plus the previous observation and the change between them for direction — check the Latest Date column for freshness. Returns a snapshot of current macro conditions."
    )]
    public Task<string> GetLatestEconomicData(
        [Description(
            "Category filter: InterestRates, YieldSpreads, CorporateBondSpreads, Inflation, Employment, GdpAndOutput, MoneySupply, Sentiment, Housing, ExchangeRates, Market (defaults to all)"
        )]
            string category = null
    )
    {
        return _runner.Execute(
            async () =>
            {
                IQueryable<FredSeries> seriesQuery;

                if (!string.IsNullOrWhiteSpace(category))
                {
                    // Reject anything that does not name a category instead of silently
                    // returning the unfiltered snapshot: a near-miss guess ("Rates",
                    // "GDP") must surface, not read as if the filter applied. The
                    // int guard exists because Enum.TryParse accepts numeric strings
                    // ("5") and would select a category by ordinal.
                    if (
                        int.TryParse(category, out _)
                        || !Enum.TryParse<FredSeriesCategory>(
                            category,
                            true,
                            out var parsedCategory
                        )
                        || !Enum.IsDefined(parsedCategory)
                    )
                    {
                        return McpOutput.InvalidArgument(
                            "category",
                            category,
                            string.Join(", ", Enum.GetNames<FredSeriesCategory>())
                        );
                    }

                    seriesQuery = _seriesRepository.GetByCategory(parsedCategory);
                }
                else
                {
                    seriesQuery = _seriesRepository.GetAll();
                }

                var allSeries = await seriesQuery
                    .OrderBy(s => s.Category)
                    .ThenBy(s => s.SeriesId)
                    .ToListAsync();

                if (allSeries.Count == 0)
                    return "No economic indicator series found in the database.";

                var latestObservations = await _observationRepository
                    .GetLatestPerSeries()
                    .ToDictionaryAsync(o => o.FredSeriesId);

                var previousObservations = await _observationRepository
                    .GetPreviousPerSeries()
                    .ToDictionaryAsync(o => o.FredSeriesId);

                var result = MarkdownTable.Start(
                    "Latest Economic Indicators:",
                    "| Series | Title | Latest Date | Value | Previous | Change | Units |",
                    "|--------|-------|-------------|-------|----------|--------|-------|"
                );

                var currentCategory = (FredSeriesCategory?)null;

                foreach (var series in allSeries)
                {
                    if (currentCategory != series.Category)
                    {
                        currentCategory = series.Category;
                        result.AppendLine($"| **{series.Category}** | | | | | | |");
                    }

                    latestObservations.TryGetValue(series.Id, out var latestObs);
                    previousObservations.TryGetValue(series.Id, out var previousObs);

                    var dateStr = McpFormat.OrDash(latestObs?.Date, "yyyy-MM-dd");
                    var valueStr = McpFormat.OrDash(latestObs?.Value, "G");
                    var previousStr = McpFormat.OrDash(previousObs?.Value, "G");

                    decimal? change =
                        latestObs?.Value != null && previousObs?.Value != null
                            ? latestObs.Value.Value - previousObs.Value.Value
                            : null;
                    var changeStr =
                        change > 0
                            ? "+" + McpFormat.Invariant(change.Value, "G")
                            : McpFormat.OrDash(change, "G");

                    result.AppendLine(
                        $"| {series.SeriesId} | {series.Title} | {dateStr} | {valueStr} | {previousStr} | {changeStr} | {series.Units} |"
                    );
                }

                return result.ToString();
            },
            "GetLatestEconomicData",
            $"category: {category}"
        );
    }

    [McpServerTool(
        Name = "SearchEconomicIndicators",
        Title = "Search Economic Indicators",
        ReadOnly = true
    )]
    [Description(
        "Search the curated set of ~40 US macro FRED series Equibles tracks (rates, inflation, employment, GDP, housing, market indicators) — not the full FRED catalog. Matches series ID, title, and category name: a category query like 'inflation' returns that whole category (CPI, PCE, PPI, breakevens), and an empty query lists every tracked series. Use this to discover what economic data is available before calling GetEconomicIndicator."
    )]
    public Task<string> SearchEconomicIndicators(
        [Description(
            "Search query — series ID, title keyword, or category name (e.g., 'inflation', 'unemployment', 'GDP', 'FEDFUNDS'). Empty lists all tracked series."
        )]
            string query,
        [Description("Maximum number of results to return (default: 20, max: 500)")]
            int maxResults = 20
    )
    {
        return _runner.Execute(
            async () =>
            {
                maxResults = McpLimit.Clamp(maxResults);

                var matches = _seriesRepository.Search(query);
                var total = await matches.CountAsync();
                var series = await matches
                    .OrderBy(s => s.Category)
                    .ThenBy(s => s.SeriesId)
                    .Take(maxResults)
                    .ToListAsync();

                var title = string.IsNullOrWhiteSpace(query)
                    ? "All tracked economic indicator series:"
                    : $"Economic indicators matching '{query}':";

                var table = MarkdownTable.Render(
                    series,
                    $"No tracked series match '{query}'. Equibles tracks a curated ~40-series US macro set - FRED series outside it are not available. Try a category name (Inflation, Employment, InterestRates, ...) or an empty query to list all tracked series.",
                    title,
                    "| Series ID | Title | Category | Frequency | Units |",
                    "|-----------|-------|----------|-----------|-------|",
                    s => $"| {s.SeriesId} | {s.Title} | {s.Category} | {s.Frequency} | {s.Units} |"
                );

                var truncation = McpOutput.TruncationNote(series.Count, total);
                if (truncation.Length > 0)
                {
                    table += Environment.NewLine + truncation + Environment.NewLine;
                }

                return table;
            },
            "SearchEconomicIndicators",
            $"query: {query}"
        );
    }

    [McpServerTool(
        Name = "GetEconomicCalendar",
        Title = "Economic Release Calendar",
        ReadOnly = true
    )]
    [Description(
        "Get the economic release calendar — scheduled (upcoming) and recent publication dates of US macro data releases, with the FRED series each release updates and an importance tier per release (High = the tier-1 scheduled market movers: CPI, PPI, Employment Situation, GDP, PCE, retail sales; Medium = other genuine scheduled prints; Low = daily rate/market levels like SOFR or VIX). FOMC meetings are NOT included — FRED's release feed has no real FOMC meeting dates; use the Federal Reserve's published meeting calendar for those. Defaults to the next 30 days. Use minImportance=high to see only the market movers, and GetEconomicIndicator to fetch a series' data after it prints."
    )]
    public Task<string> GetEconomicCalendar(
        [Description("Start date in YYYY-MM-DD format (defaults to today, UTC)")]
            string startDate = null,
        [Description("End date in YYYY-MM-DD format (defaults to 30 days after the start date)")]
            string endDate = null,
        [Description(
            "Minimum importance tier to include: low, medium, or high (defaults to low = everything)"
        )]
            string minImportance = null,
        [Description(
            "Maximum number of release dates to return (default: 100, max: 500, chronological)"
        )]
            int maxResults = 100
    )
    {
        return _runner.Execute(
            async () =>
            {
                // The int guard exists because Enum.TryParse accepts numeric strings:
                // minImportance="5" would parse to an undefined tier that filters out
                // everything and reads as a factual "no releases" answer.
                var minTier = FredReleaseImportance.Low;
                if (
                    !string.IsNullOrWhiteSpace(minImportance)
                    && (
                        int.TryParse(minImportance, out _)
                        || !Enum.TryParse(minImportance, true, out minTier)
                        || !Enum.IsDefined(minTier)
                    )
                )
                {
                    return McpOutput.InvalidArgument(
                        "minImportance",
                        minImportance,
                        "low, medium, high"
                    );
                }

                var today = DateOnly.FromDateTime(DateTime.UtcNow);
                var start = today;
                if (!string.IsNullOrWhiteSpace(startDate))
                {
                    if (!McpOutput.TryParseDate(startDate, out var parsedStart))
                        return McpOutput.InvalidArgument("startDate", startDate, "yyyy-MM-dd");
                    start = DateOnly.FromDateTime(parsedStart);
                }

                var end = start.AddDays(30);
                if (!string.IsNullOrWhiteSpace(endDate))
                {
                    if (!McpOutput.TryParseDate(endDate, out var parsedEnd))
                        return McpOutput.InvalidArgument("endDate", endDate, "yyyy-MM-dd");
                    end = DateOnly.FromDateTime(parsedEnd);
                }

                if (start > end)
                    return $"startDate ({start:yyyy-MM-dd}) is after endDate ({end:yyyy-MM-dd}). Swap or correct the dates.";

                maxResults = McpLimit.Clamp(maxResults);

                var inRange = _releaseDateRepository
                    .GetInRange(start, end)
                    .Where(d => d.FredRelease.Importance >= minTier);

                var total = await inRange.CountAsync();

                var entries = await inRange
                    .OrderBy(d => d.Date)
                    .ThenByDescending(d => d.FredRelease.Importance)
                    .ThenBy(d => d.FredRelease.Name)
                    .Take(maxResults)
                    .Select(d => new
                    {
                        d.Date,
                        ReleaseName = d.FredRelease.Name,
                        d.FredRelease.Importance,
                        Series = d
                            .FredRelease.Series.OrderBy(s => s.SeriesId)
                            .Select(s => s.SeriesId)
                            .ToList(),
                    })
                    .ToListAsync();

                // A truncated calendar must not present the requested range as fully
                // covered — otherwise the tail of the window silently reads as
                // "nothing scheduled". Name the last date actually shown in the header
                // and add the standard footer.
                var truncated = entries.Count < total;
                var title = truncated
                    ? $"Economic release calendar ({start:yyyy-MM-dd} to {end:yyyy-MM-dd} requested; truncated at {entries.Count} rows, shown only through {entries[^1].Date:yyyy-MM-dd}):"
                    : $"Economic release calendar ({start:yyyy-MM-dd} to {end:yyyy-MM-dd}):";

                var table = MarkdownTable.Render(
                    entries,
                    $"No economic releases between {start:yyyy-MM-dd} and {end:yyyy-MM-dd}.",
                    title,
                    "| Date | Release | Importance | Series Updated |",
                    "|------|---------|------------|----------------|",
                    e =>
                        $"| {e.Date:yyyy-MM-dd} | {e.ReleaseName} | {e.Importance} | {string.Join(", ", e.Series)} |"
                );

                if (truncated)
                {
                    table +=
                        Environment.NewLine
                        + McpOutput.TruncationNote(entries.Count, total)
                        + Environment.NewLine;
                }

                return table;
            },
            "GetEconomicCalendar",
            $"startDate: {startDate}, endDate: {endDate}, minImportance: {minImportance}"
        );
    }
}
