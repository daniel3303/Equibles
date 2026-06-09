using System.ComponentModel;
using Equibles.CommonStocks.Repositories;
using Equibles.CommonStocks.Repositories.Extensions;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.BusinessLogic.Extensions;
using Equibles.Finra.Data.Models;
using Equibles.Finra.Repositories;
using Equibles.Mcp;
using Equibles.Mcp.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace Equibles.Finra.Mcp.Tools;

[McpServerToolType]
public class OffExchangeVolumeTools
{
    private readonly OffExchangeVolumeRepository _offExchangeVolumeRepository;
    private readonly CommonStockRepository _commonStockRepository;
    private readonly McpToolRunner _runner;

    public OffExchangeVolumeTools(
        OffExchangeVolumeRepository offExchangeVolumeRepository,
        CommonStockRepository commonStockRepository,
        ErrorManager errorManager,
        ILogger<OffExchangeVolumeTools> logger
    )
    {
        _offExchangeVolumeRepository = offExchangeVolumeRepository;
        _commonStockRepository = commonStockRepository;
        _runner = new McpToolRunner(logger, errorManager.AsMcpErrorReporter());
    }

    [McpServerTool(Name = "GetOffExchangeVolume")]
    [Description(
        "Get weekly off-exchange (dark pool / OTC) trading volume for a stock from the FINRA OTC/ATS Transparency data. "
            + "Each week shows ATS (alternating trading system / dark pool) volume and trade count, non-ATS OTC volume and trade count, "
            + "and the total off-exchange volume (ATS + non-ATS OTC). The FINRA file does not include consolidated tape volume, so the "
            + "off-exchange share of total market volume is not reported here; compute that share elsewhere against a consolidated-volume source."
    )]
    public Task<string> GetOffExchangeVolume(
        [Description("Stock ticker symbol (e.g., AAPL, GME, TSLA)")] string ticker,
        [Description("Start date in YYYY-MM-DD format (defaults to 6 months ago)")]
            string startDate = null,
        [Description("End date in YYYY-MM-DD format (defaults to latest available)")]
            string endDate = null,
        [Description("Maximum number of weeks to return (default: 26, newest first)")]
            int maxResults = 26
    )
    {
        return _runner.Execute(
            async () =>
            {
                var (stock, stockError) = await _commonStockRepository.ResolveByTicker(ticker);
                if (stockError != null)
                    return stockError;

                var (start, end) = McpToolExecutor.ParseDateRange(
                    startDate,
                    endDate,
                    McpToolExecutor.UtcMonthsAgo(6)
                );

                var startWeek = DateOnly.FromDateTime(start.ToDateTime(TimeOnly.MinValue));
                var endWeek = DateOnly.FromDateTime(end.ToDateTime(TimeOnly.MinValue));

                maxResults = McpLimit.Clamp(maxResults);

                var records = await _offExchangeVolumeRepository
                    .GetHistoryByStock(stock)
                    .Where(d => d.WeekStartDate >= startWeek && d.WeekStartDate <= endWeek)
                    .OrderByDescending(d => d.WeekStartDate)
                    .Take(maxResults)
                    .ToListAsync();

                return MarkdownTable.Render(
                    records.OrderBy(r => r.WeekStartDate).ToList(),
                    $"No off-exchange volume data found for {stock.Ticker} in the specified date range.",
                    $"Weekly off-exchange (dark pool / OTC) volume for {stock.Ticker} ({stock.Name}):",
                    "| Week Start | ATS Volume | ATS Trades | Non-ATS OTC Volume | Non-ATS OTC Trades | Total Off-Exchange Volume |",
                    "|------------|-----------|-----------|-------------------|-------------------|--------------------------|",
                    r => RenderOffExchangeRow($"{r.WeekStartDate:yyyy-MM-dd}", r)
                );
            },
            "GetOffExchangeVolume",
            $"ticker: {ticker}"
        );
    }

    // Render with InvariantCulture so the MCP markdown does not fork the separators by host
    // locale (e.g. de-DE would render 5.000.000 instead of 5,000,000). Total off-exchange
    // volume is the sum of ATS and non-ATS OTC volume; the share of consolidated tape volume
    // is intentionally omitted because the FINRA file carries no consolidated total.
    private static string RenderOffExchangeRow(string leadCell, OffExchangeVolume r)
    {
        var totalOffExchangeVolume = r.AtsVolume + r.NonAtsOtcVolume;
        return $"| {leadCell} | {McpFormat.WholeNumber(r.AtsVolume)} | {McpFormat.WholeNumber(r.AtsTradeCount)} | {McpFormat.WholeNumber(r.NonAtsOtcVolume)} | {McpFormat.WholeNumber(r.NonAtsOtcTradeCount)} | {McpFormat.WholeNumber(totalOffExchangeVolume)} |";
    }
}
