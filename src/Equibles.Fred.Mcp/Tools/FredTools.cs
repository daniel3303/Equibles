using System.ComponentModel;
using System.Text;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Equibles.Fred.Data.Models;
using Equibles.Fred.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace Equibles.Fred.Mcp.Tools;

[McpServerToolType]
public class FredTools {
    private readonly FredSeriesRepository _seriesRepository;
    private readonly FredObservationRepository _observationRepository;
    private readonly ErrorManager _errorManager;
    private readonly ILogger<FredTools> _logger;

    public FredTools(
        FredSeriesRepository seriesRepository,
        FredObservationRepository observationRepository,
        ErrorManager errorManager,
        ILogger<FredTools> logger
    ) {
        _seriesRepository = seriesRepository;
        _observationRepository = observationRepository;
        _errorManager = errorManager;
        _logger = logger;
    }

    [McpServerTool(Name = "GetEconomicIndicator")]
    [Description("Get time series data for a FRED economic indicator. Returns historical observations for indicators like FEDFUNDS (fed funds rate), CPIAUCSL (CPI inflation), UNRATE (unemployment), GDP, T10Y2Y (yield spread), VIXCLS (VIX), SP500, MORTGAGE30US, M2SL (money supply), and more. Use SearchEconomicIndicators to find available series.")]
    public async Task<string> GetEconomicIndicator(
        [Description("FRED series ID (e.g., FEDFUNDS, CPIAUCSL, UNRATE, GDP, T10Y2Y, VIXCLS)")] string seriesId,
        [Description("Start date in YYYY-MM-DD format (defaults to 1 year ago)")] string startDate = null,
        [Description("End date in YYYY-MM-DD format (defaults to latest available)")] string endDate = null,
        [Description("Maximum number of observations to return (default: 100, newest first)")] int maxResults = 100
    ) {
        try {
            var series = await _seriesRepository.GetBySeriesId(seriesId.ToUpper())
                .FirstOrDefaultAsync();

            if (series == null) return $"Series '{seriesId}' not found. Use SearchEconomicIndicators to find available series.";

            var start = !string.IsNullOrEmpty(startDate) && DateOnly.TryParse(startDate, out var parsedStart)
                ? parsedStart
                : DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-1));

            var end = !string.IsNullOrEmpty(endDate) && DateOnly.TryParse(endDate, out var parsedEnd)
                ? parsedEnd
                : DateOnly.FromDateTime(DateTime.UtcNow);

            var observations = await _observationRepository.GetBySeries(series, start, end)
                .OrderByDescending(o => o.Date)
                .Take(maxResults)
                .ToListAsync();

            if (observations.Count == 0) return $"No observations found for {series.SeriesId} ({series.Title}) in the specified date range.";

            var result = new StringBuilder();
            result.AppendLine($"{series.Title} ({series.SeriesId})");
            result.AppendLine($"Units: {series.Units} | Frequency: {series.Frequency} | Seasonal Adj: {series.SeasonalAdjustment}");
            result.AppendLine();
            result.AppendLine("| Date | Value |");
            result.AppendLine("|------|-------|");

            foreach (var obs in observations.OrderBy(o => o.Date)) {
                var valueStr = obs.Value.HasValue ? obs.Value.Value.ToString("G") : "N/A";
                result.AppendLine($"| {obs.Date:yyyy-MM-dd} | {valueStr} |");
            }

            return result.ToString();
        } catch (Exception ex) {
            _logger.LogError(ex, "GetEconomicIndicator failed for {SeriesId}", seriesId);
            try { await _errorManager.Create(ErrorSource.McpTool, "GetEconomicIndicator", ex.Message, ex.StackTrace, $"seriesId: {seriesId}"); } catch { }
            return "An error occurred while fetching economic indicator data. Please try again.";
        }
    }

    [McpServerTool(Name = "GetLatestEconomicData")]
    [Description("Get the latest values for key economic indicators across categories: interest rates, yield spreads, inflation, employment, GDP, money supply, sentiment, housing, exchange rates, and market indicators. Returns a snapshot of current macro conditions.")]
    public async Task<string> GetLatestEconomicData(
        [Description("Category filter: InterestRates, YieldSpreads, CorporateBondSpreads, Inflation, Employment, GdpAndOutput, MoneySupply, Sentiment, Housing, ExchangeRates, Market (defaults to all)")] string category = null
    ) {
        try {
            IQueryable<FredSeries> seriesQuery;

            if (!string.IsNullOrEmpty(category) && Enum.TryParse<FredSeriesCategory>(category, true, out var parsedCategory)) {
                seriesQuery = _seriesRepository.GetByCategory(parsedCategory);
            } else {
                seriesQuery = _seriesRepository.GetAll();
            }

            var allSeries = await seriesQuery.OrderBy(s => s.Category).ThenBy(s => s.SeriesId).ToListAsync();

            if (allSeries.Count == 0) return "No economic indicator series found in the database.";

            // Single query to get latest observation per series
            var latestObservations = await _observationRepository.GetLatestPerSeries()
                .ToDictionaryAsync(o => o.FredSeriesId);

            var result = new StringBuilder();
            result.AppendLine("Latest Economic Indicators:");
            result.AppendLine();
            result.AppendLine("| Series | Title | Latest Date | Value | Units |");
            result.AppendLine("|--------|-------|-------------|-------|-------|");

            var currentCategory = (FredSeriesCategory?)null;

            foreach (var series in allSeries) {
                if (currentCategory != series.Category) {
                    currentCategory = series.Category;
                    result.AppendLine($"| **{series.Category}** | | | | |");
                }

                latestObservations.TryGetValue(series.Id, out var latestObs);

                var dateStr = latestObs?.Date.ToString("yyyy-MM-dd") ?? "—";
                var valueStr = latestObs?.Value?.ToString("G") ?? "—";

                result.AppendLine($"| {series.SeriesId} | {series.Title} | {dateStr} | {valueStr} | {series.Units} |");
            }

            return result.ToString();
        } catch (Exception ex) {
            _logger.LogError(ex, "GetLatestEconomicData failed");
            try { await _errorManager.Create(ErrorSource.McpTool, "GetLatestEconomicData", ex.Message, ex.StackTrace, $"category: {category}"); } catch { }
            return "An error occurred while fetching latest economic data. Please try again.";
        }
    }

    [McpServerTool(Name = "SearchEconomicIndicators")]
    [Description("Search for available FRED economic indicator series by name or series ID. Returns matching series with their metadata. Use this to discover what economic data is available before calling GetEconomicIndicator.")]
    public async Task<string> SearchEconomicIndicators(
        [Description("Search query — series ID or title keyword (e.g., 'inflation', 'unemployment', 'GDP', 'FEDFUNDS')")] string query,
        [Description("Maximum number of results to return (default: 20)")] int maxResults = 20
    ) {
        try {
            var series = await _seriesRepository.Search(query)
                .OrderBy(s => s.Category)
                .ThenBy(s => s.SeriesId)
                .Take(maxResults)
                .ToListAsync();

            if (series.Count == 0) return $"No series found matching '{query}'.";

            var result = new StringBuilder();
            result.AppendLine($"Economic indicators matching '{query}':");
            result.AppendLine();
            result.AppendLine("| Series ID | Title | Category | Frequency | Units |");
            result.AppendLine("|-----------|-------|----------|-----------|-------|");

            foreach (var s in series) {
                result.AppendLine($"| {s.SeriesId} | {s.Title} | {s.Category} | {s.Frequency} | {s.Units} |");
            }

            return result.ToString();
        } catch (Exception ex) {
            _logger.LogError(ex, "SearchEconomicIndicators failed for query {Query}", query);
            try { await _errorManager.Create(ErrorSource.McpTool, "SearchEconomicIndicators", ex.Message, ex.StackTrace, $"query: {query}"); } catch { }
            return "An error occurred while searching economic indicators. Please try again.";
        }
    }
}
