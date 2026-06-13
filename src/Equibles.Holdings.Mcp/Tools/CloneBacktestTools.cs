using System.ComponentModel;
using System.Globalization;
using System.Text;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.BusinessLogic.Extensions;
using Equibles.Holdings.BusinessLogic;
using Equibles.Holdings.BusinessLogic.Models;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Repositories;
using Equibles.Mcp;
using Equibles.Mcp.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace Equibles.Holdings.Mcp.Tools;

[McpServerToolType]
public class CloneBacktestTools
{
    private const int DefaultWindowYears = 3;
    private const int MinWindowYears = 1;
    private const int MaxWindowYears = 20;

    private readonly HoldingsCloneBacktestProvider _backtestProvider;
    private readonly InstitutionalHolderRepository _holderRepository;
    private readonly McpToolRunner _runner;

    public CloneBacktestTools(
        HoldingsCloneBacktestProvider backtestProvider,
        InstitutionalHolderRepository holderRepository,
        ErrorManager errorManager,
        ILogger<CloneBacktestTools> logger
    )
    {
        _backtestProvider = backtestProvider;
        _holderRepository = holderRepository;
        _runner = new McpToolRunner(logger, errorManager.AsMcpErrorReporter());
    }

    [McpServerTool(Name = "GetFundCloneBacktest")]
    [Description(
        "Backtest how cloning an institutional filer's reported 13F portfolio would have performed against a market benchmark over a trailing window. Reconstructs the filer's portfolio at each quarterly 13F snapshot, rebalances on the SEC filing lag (so the simulation uses only information available at the time), and values it forward against the benchmark. Returns total return, annualized return (CAGR), and max drawdown for both the cloned portfolio and the benchmark, plus the alpha between them. Use this to answer 'how would cloning fund X have performed against the market'."
    )]
    public Task<string> GetFundCloneBacktest(
        [Description("Institution name or SEC CIK (e.g., 'Berkshire Hathaway' or '0001067983')")]
            string institution,
        [Description("Benchmark ticker to compare against (default: SPY)")]
            string benchmark = "SPY",
        [Description("Trailing window length in years (default: 3, range 1-20)")]
            int windowYears = DefaultWindowYears
    )
    {
        return _runner.Execute(
            async () =>
            {
                windowYears = Math.Clamp(windowYears, MinWindowYears, MaxWindowYears);

                var holder = await _holderRepository
                    .SearchNameOrCik(institution)
                    .OrderBy(h => h.Name)
                    .FirstOrDefaultAsync();
                if (holder == null)
                    return $"No institution found matching '{institution}'.";

                var to = DateOnly.FromDateTime(DateTime.UtcNow);
                var from = to.AddYears(-windowYears);

                var outcome = await _backtestProvider.Run(holder.Cik, from, to, benchmark);

                if (outcome.BenchmarkNotFound)
                    return $"Benchmark ticker '{outcome.Benchmark}' is not known.";

                if (outcome.Result.Points.Count == 0)
                    return $"Could not backtest {holder.Name} (CIK {holder.Cik}): "
                        + (outcome.Result.Reason ?? "no data available for the requested window.");

                return Render(holder, outcome);
            },
            "GetFundCloneBacktest",
            $"institution: {institution}"
        );
    }

    private static string Render(InstitutionalHolder holder, CloneBacktestOutcome outcome)
    {
        var result = outcome.Result;
        var portfolio = result.PortfolioSummary;
        var benchmark = result.BenchmarkSummary;
        var alpha = portfolio.TotalReturnPercent - benchmark.TotalReturnPercent;

        var output = new StringBuilder();
        output.AppendLine(
            $"Clone backtest of {holder.Name} (CIK {holder.Cik}) vs {outcome.BenchmarkName} "
                + $"({outcome.Benchmark}), {FormatDate(result.StartDate)} to {FormatDate(result.EndDate)}:"
        );
        output.AppendLine();
        output.AppendLine("| Strategy | Total return | CAGR | Max drawdown |");
        output.AppendLine("|---|---|---|---|");
        output.AppendLine(
            $"| Cloned portfolio | {FormatPercent(portfolio.TotalReturnPercent)} | "
                + $"{FormatCagr(portfolio.CagrPercent)} | {FormatPercent(portfolio.MaxDrawdownPercent)} |"
        );
        output.AppendLine(
            $"| {outcome.Benchmark} | {FormatPercent(benchmark.TotalReturnPercent)} | "
                + $"{FormatCagr(benchmark.CagrPercent)} | {FormatPercent(benchmark.MaxDrawdownPercent)} |"
        );
        output.AppendLine();
        output.AppendLine(
            $"Alpha vs benchmark (total return): {FormatPercent(alpha)}. "
                + $"{result.Points.Count} daily points simulated."
        );
        return output.ToString();
    }

    // Signed percentage with one decimal, invariant culture so MCP markdown stays locale-stable.
    private static string FormatPercent(decimal value) =>
        McpFormat.Invariant(value, "+0.0;-0.0;0.0") + "%";

    // CagrPercent is null when the window is too short to annualize meaningfully.
    private static string FormatCagr(decimal? value) =>
        value.HasValue ? FormatPercent(value.Value) : "—";

    private static string FormatDate(DateOnly date) =>
        date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
