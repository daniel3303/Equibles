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

    [McpServerTool(Name = "GetEconomicIndicator")]
    [Description(
        "Get time series data for a FRED economic indicator. Returns historical observations for indicators like FEDFUNDS (fed funds rate), CPIAUCSL (CPI inflation), UNRATE (unemployment), GDP, T10Y2Y (yield spread), VIXCLS (VIX), SP500, MORTGAGE30US, M2SL (money supply), and more. Use SearchEconomicIndicators to find available series."
    )]
    public Task<string> GetEconomicIndicator(
        [Description("FRED series ID (e.g., FEDFUNDS, CPIAUCSL, UNRATE, GDP, T10Y2Y, VIXCLS)")]
            string seriesId,
        [Description("Start date in YYYY-MM-DD format (defaults to 1 year ago)")]
            string startDate = null,
        [Description("End date in YYYY-MM-DD format (defaults to latest available)")]
            string endDate = null,
        [Description("Maximum number of observations to return (default: 100, newest first)")]
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

                var (start, end) = McpToolExecutor.ParseDateRange(
                    startDate,
                    endDate,
                    McpToolExecutor.UtcYearsAgo(1)
                );

                maxResults = McpLimit.Clamp(maxResults);

                var observations = await _observationRepository
                    .GetBySeries(series, start, end)
                    .OrderByDescending(o => o.Date)
                    .Take(maxResults)
                    .ToListAsync();

                return MarkdownTable.Render(
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
            },
            "GetEconomicIndicator",
            $"seriesId: {seriesId}"
        );
    }

    [McpServerTool(Name = "GetLatestEconomicData")]
    [Description(
        "Get the latest values for key economic indicators across categories: interest rates, yield spreads, inflation, employment, GDP, money supply, sentiment, housing, exchange rates, and market indicators. Returns a snapshot of current macro conditions."
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

                if (
                    !string.IsNullOrEmpty(category)
                    && Enum.TryParse<FredSeriesCategory>(category, true, out var parsedCategory)
                )
                {
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

                var result = MarkdownTable.Start(
                    "Latest Economic Indicators:",
                    "| Series | Title | Latest Date | Value | Units |",
                    "|--------|-------|-------------|-------|-------|"
                );

                var currentCategory = (FredSeriesCategory?)null;

                foreach (var series in allSeries)
                {
                    if (currentCategory != series.Category)
                    {
                        currentCategory = series.Category;
                        result.AppendLine($"| **{series.Category}** | | | | |");
                    }

                    latestObservations.TryGetValue(series.Id, out var latestObs);

                    var dateStr = McpFormat.OrDash(latestObs?.Date, "yyyy-MM-dd");
                    var valueStr = McpFormat.OrDash(latestObs?.Value, "G");

                    result.AppendLine(
                        $"| {series.SeriesId} | {series.Title} | {dateStr} | {valueStr} | {series.Units} |"
                    );
                }

                return result.ToString();
            },
            "GetLatestEconomicData",
            $"category: {category}"
        );
    }

    [McpServerTool(Name = "SearchEconomicIndicators")]
    [Description(
        "Search for available FRED economic indicator series by name or series ID. Returns matching series with their metadata. Use this to discover what economic data is available before calling GetEconomicIndicator."
    )]
    public Task<string> SearchEconomicIndicators(
        [Description(
            "Search query — series ID or title keyword (e.g., 'inflation', 'unemployment', 'GDP', 'FEDFUNDS')"
        )]
            string query,
        [Description("Maximum number of results to return (default: 20)")] int maxResults = 20
    )
    {
        return _runner.Execute(
            async () =>
            {
                maxResults = McpLimit.Clamp(maxResults);

                var series = await _seriesRepository
                    .Search(query)
                    .OrderBy(s => s.Category)
                    .ThenBy(s => s.SeriesId)
                    .Take(maxResults)
                    .ToListAsync();

                return MarkdownTable.Render(
                    series,
                    $"No series found matching '{query}'.",
                    $"Economic indicators matching '{query}':",
                    "| Series ID | Title | Category | Frequency | Units |",
                    "|-----------|-------|----------|-----------|-------|",
                    s => $"| {s.SeriesId} | {s.Title} | {s.Category} | {s.Frequency} | {s.Units} |"
                );
            },
            "SearchEconomicIndicators",
            $"query: {query}"
        );
    }

    [McpServerTool(Name = "GetEconomicCalendar")]
    [Description(
        "Get the economic release calendar — scheduled (upcoming) and recent publication dates of US macro data releases (CPI, Employment Situation, GDP, and the other tracked indicators), with the FRED series each release updates. Defaults to the next 30 days. Use GetEconomicIndicator to fetch a series' data after it prints."
    )]
    public Task<string> GetEconomicCalendar(
        [Description("Start date in YYYY-MM-DD format (defaults to today, UTC)")]
            string startDate = null,
        [Description("End date in YYYY-MM-DD format (defaults to 30 days after the start date)")]
            string endDate = null,
        [Description("Maximum number of release dates to return (default: 100, chronological)")]
            int maxResults = 100
    )
    {
        return _runner.Execute(
            async () =>
            {
                var today = DateOnly.FromDateTime(DateTime.UtcNow);
                var start = McpToolExecutor.ParseDateOr(startDate, today);
                var end = McpToolExecutor.ParseDateOr(endDate, start.AddDays(30));

                maxResults = McpLimit.Clamp(maxResults);

                var entries = await _releaseDateRepository
                    .GetInRange(start, end)
                    .OrderBy(d => d.Date)
                    .ThenBy(d => d.FredRelease.Name)
                    .Take(maxResults)
                    .Select(d => new
                    {
                        d.Date,
                        ReleaseName = d.FredRelease.Name,
                        Series = d
                            .FredRelease.Series.OrderBy(s => s.SeriesId)
                            .Select(s => s.SeriesId)
                            .ToList(),
                    })
                    .ToListAsync();

                return MarkdownTable.Render(
                    entries,
                    $"No economic releases between {start:yyyy-MM-dd} and {end:yyyy-MM-dd}.",
                    $"Economic release calendar ({start:yyyy-MM-dd} to {end:yyyy-MM-dd}):",
                    "| Date | Release | Series Updated |",
                    "|------|---------|----------------|",
                    e =>
                        $"| {e.Date:yyyy-MM-dd} | {e.ReleaseName} | {string.Join(", ", e.Series)} |"
                );
            },
            "GetEconomicCalendar",
            $"startDate: {startDate}, endDate: {endDate}"
        );
    }
}
