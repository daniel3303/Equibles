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

    [McpServerTool(
        Name = "GetFundCloneBacktest",
        Title = "13F Portfolio Clone Backtest",
        ReadOnly = true
    )]
    [Description(
        "Backtest how cloning an institutional filer's reported 13F portfolio would have performed against a market benchmark, either over a trailing window (windowYears) or an explicit fromDate/toDate range. Reconstructs the filer's portfolio at each quarterly 13F snapshot, rebalances on the SEC filing lag (so the simulation uses only information available at the time), and values it forward against the benchmark. Returns total return, annualized return (CAGR), and max drawdown for both the cloned portfolio and the benchmark, plus the alpha between them. Use this to answer 'how would cloning fund X have performed against the market'."
    )]
    public Task<string> GetFundCloneBacktest(
        [Description(
            "Institution name or SEC CIK (e.g., 'Berkshire Hathaway', '1067983', or zero-padded '0001067983'; ambiguous names resolve to the largest 13F filer)"
        )]
            string institution,
        [Description("Benchmark ticker to compare against (default: SPY)")]
            string benchmark = "SPY",
        [Description(
            "Trailing window length in years anchored at today (default: 3, clamped to 1-20; ignored when fromDate/toDate are supplied)"
        )]
            int windowYears = DefaultWindowYears,
        [Description(
            "Optional window start in YYYY-MM-DD format for an anchored historical backtest (e.g. 2015-01-01); overrides windowYears"
        )]
            string fromDate = null,
        [Description(
            "Optional window end in YYYY-MM-DD format (defaults to today when only fromDate is given)"
        )]
            string toDate = null
    )
    {
        return _runner.Execute(
            async () =>
            {
                var requestedYears = windowYears;
                windowYears = Math.Clamp(windowYears, MinWindowYears, MaxWindowYears);

                DateOnly? explicitFrom = null;
                DateOnly? explicitTo = null;
                if (!string.IsNullOrWhiteSpace(fromDate))
                {
                    if (!McpOutput.TryParseDate(fromDate, out var parsedFrom))
                        return McpOutput.InvalidArgument("fromDate", fromDate, "YYYY-MM-DD");
                    explicitFrom = DateOnly.FromDateTime(parsedFrom);
                }
                if (!string.IsNullOrWhiteSpace(toDate))
                {
                    if (!McpOutput.TryParseDate(toDate, out var parsedTo))
                        return McpOutput.InvalidArgument("toDate", toDate, "YYYY-MM-DD");
                    explicitTo = DateOnly.FromDateTime(parsedTo);
                }
                if (explicitTo.HasValue && !explicitFrom.HasValue)
                    return "toDate requires fromDate — pass both to anchor the window.";

                // Largest 13F filer first: an ambiguous name ("Fidelity") must backtest the
                // flagship manager, not whichever small RIA sorts first alphabetically.
                var matches = await _holderRepository.SearchNameOrCikLargestFirst(institution, 4);
                if (matches.Count == 0)
                    return $"No institution found matching '{institution}'.";
                var holder = matches[0];

                var to = explicitTo ?? DateOnly.FromDateTime(DateTime.UtcNow);
                var from = explicitFrom ?? to.AddYears(-windowYears);
                if (from >= to)
                    return $"fromDate {FormatDate(from)} must be before toDate {FormatDate(to)}.";

                var outcome = await _backtestProvider.Run(holder.Cik, from, to, benchmark);

                if (outcome.BenchmarkNotFound)
                    return $"Benchmark ticker '{outcome.Benchmark}' is not known.";

                if (outcome.Result.Points.Count == 0)
                    return $"Could not backtest {holder.Name} (CIK {holder.Cik}): "
                        + (outcome.Result.Reason ?? "no data available for the requested window.");

                var notes = BuildNotes(
                    matches,
                    holder,
                    outcome,
                    from,
                    explicitFrom.HasValue,
                    requestedYears,
                    windowYears
                );
                return Render(holder, outcome, notes);
            },
            "GetFundCloneBacktest",
            $"institution: {institution}"
        );
    }

    // The annotation lines that keep the numbers honest: which filer an ambiguous name
    // resolved to (and the alternates), a clamped windowYears, and a window shortened by the
    // available 13F history — an LLM comparing two funds at "windowYears: 10" must see when
    // one simulation actually covers 6 years.
    private static List<string> BuildNotes(
        List<InstitutionalHolder> matches,
        InstitutionalHolder holder,
        CloneBacktestOutcome outcome,
        DateOnly requestedFrom,
        bool explicitWindow,
        int requestedYears,
        int clampedYears
    )
    {
        var notes = new List<string>();
        if (matches.Count > 1)
            notes.Add(
                $"Note: matched {holder.Name} (CIK {holder.Cik}, largest 13F filer of the matches); other matches: "
                    + $"{string.Join(", ", matches.Skip(1).Select(m => $"{m.Name} (CIK {m.Cik})"))}."
            );
        if (!explicitWindow && requestedYears != clampedYears)
            notes.Add(
                $"Note: windowYears {requestedYears} is outside 1-20 and was clamped to {clampedYears}."
            );
        var result = outcome.Result;
        if (result.StartDate > requestedFrom)
        {
            var coveredYears = (result.EndDate.DayNumber - result.StartDate.DayNumber) / 365.25;
            notes.Add(
                $"Note: requested window starts {FormatDate(requestedFrom)}, but the filer's usable 13F/price history begins "
                    + $"{FormatDate(result.StartDate)} — the simulation covers ~{McpFormat.Invariant(coveredYears, "0.0")} years."
            );
        }
        return notes;
    }

    private static string Render(
        InstitutionalHolder holder,
        CloneBacktestOutcome outcome,
        List<string> notes
    )
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
        foreach (var note in notes)
            output.AppendLine(note);
        output.AppendLine();
        output.AppendLine("| Strategy | Total return | CAGR | Max drawdown |");
        output.AppendLine("|---|---|---|---|");
        output.AppendLine(
            $"| Cloned portfolio | {FormatPercent(portfolio.TotalReturnPercent)} | "
                + $"{FormatCagr(portfolio.CagrPercent)} | {FormatDrawdown(portfolio.MaxDrawdownPercent)} |"
        );
        output.AppendLine(
            $"| {outcome.Benchmark} | {FormatPercent(benchmark.TotalReturnPercent)} | "
                + $"{FormatCagr(benchmark.CagrPercent)} | {FormatDrawdown(benchmark.MaxDrawdownPercent)} |"
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

    // Max drawdown is a positive loss magnitude; rendering it through the signed formatter
    // produced "+20.2%", which reads as a gain. Unsigned under the "Max drawdown" header.
    private static string FormatDrawdown(decimal value) => McpFormat.Invariant(value, "0.0") + "%";

    // CagrPercent is null when the window is too short to annualize meaningfully.
    private static string FormatCagr(decimal? value) =>
        value.HasValue ? FormatPercent(value.Value) : "—";

    private static string FormatDate(DateOnly date) =>
        date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
