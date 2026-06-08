using System.ComponentModel;
using Equibles.CommonStocks.Repositories;
using Equibles.CommonStocks.Repositories.Extensions;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.BusinessLogic.Extensions;
using Equibles.Mcp;
using Equibles.Mcp.Helpers;
using Equibles.Sec.Data.Models;
using Equibles.Sec.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace Equibles.Sec.Mcp.Tools;

[McpServerToolType]
public class NportTools
{
    private readonly NportFilingRepository _nportRepository;
    private readonly CommonStockRepository _commonStockRepository;
    private readonly McpToolRunner _runner;

    public NportTools(
        NportFilingRepository nportRepository,
        CommonStockRepository commonStockRepository,
        ErrorManager errorManager,
        ILogger<NportTools> logger
    )
    {
        _nportRepository = nportRepository;
        _commonStockRepository = commonStockRepository;
        _runner = new McpToolRunner(logger, errorManager.AsMcpErrorReporter());
    }

    [McpServerTool(Name = "GetFundHoldings")]
    [Description(
        "Get the portfolio holdings of a registered investment company (mutual fund or ETF) from its most recent SEC Form NPORT-P monthly report. Returns the fund's series, reporting period and net assets, followed by its largest holdings — issuer name, CUSIP, position size, U.S.-dollar value and share of net assets, with the asset category (e.g. EC equity-common, DBT debt, DE derivative). Use this to see what a fund or ETF owns. Only registered funds file NPORT-P; operating companies will return no data."
    )]
    public Task<string> GetFundHoldings(
        [Description("Fund or ETF ticker symbol (e.g., SPY, VOO)")] string ticker,
        [Description("Maximum number of holdings to return, largest first (default: 20)")]
            int maxResults = 20
    )
    {
        return _runner.Execute(
            async () =>
            {
                var (stock, stockError) = await _commonStockRepository.ResolveByTicker(ticker);
                if (stockError != null)
                    return stockError;

                var filing = await _nportRepository
                    .GetByStock(stock)
                    .Include(f => f.Holdings)
                    .OrderByDescending(f => f.FilingDate)
                    .FirstOrDefaultAsync();

                if (filing == null)
                    return $"No Form NPORT-P portfolio reports found for {ticker}.";

                var holdings = filing
                    .Holdings.OrderByDescending(h => h.ValueUsd)
                    .Take(maxResults)
                    .ToList();

                var result = MarkdownTable.Start(
                    $"Portfolio holdings for {filing.SeriesName ?? stock.Name} ({ticker}) — "
                        + $"reported {filing.ReportPeriodDate:yyyy-MM-dd}, net assets ${FormatAmount(filing.NetAssets)}, "
                        + $"{filing.Holdings.Count} total holdings, showing the largest {holdings.Count}:",
                    "| Holding | CUSIP | Balance | Units | Value (USD) | % Net Assets | Category | Country |",
                    "|---------|-------|---------|-------|-------------|--------------|----------|---------|"
                );

                result.AppendRows(
                    holdings,
                    h =>
                        $"| {h.Name ?? "-"} | {h.Cusip ?? "-"} | {FormatAmount(h.Balance)} | {h.Units ?? "-"} | ${FormatAmount(h.ValueUsd)} | {FormatPercent(h.PercentValue)} | {h.AssetCategory ?? "-"} | {h.InvestmentCountry ?? "-"} |"
                );

                return result.ToString();
            },
            "GetFundHoldings",
            $"ticker: {ticker}"
        );
    }

    private static string FormatAmount(decimal value) => McpFormat.Invariant(value, "N2");

    private static string FormatPercent(decimal value) => McpFormat.Invariant(value, "N2") + "%";
}
