using System.ComponentModel;
using System.Globalization;
using System.Text;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.BusinessLogic.Extensions;
using Equibles.Holdings.BusinessLogic;
using Equibles.Holdings.BusinessLogic.Models;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Repositories;
using Equibles.Holdings.Repositories.Models;
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
        Name = "GetInstitutionCloneBacktest",
        Title = "13F Portfolio Clone Backtest",
        ReadOnly = true
    )]
    [Description(
        "Backtest how cloning an institutional filer's reported 13F portfolio would have performed against a market benchmark, either over a trailing window (windowYears) or an explicit fromDate/toDate range. Reconstructs the filer's portfolio at each quarterly 13F snapshot, rebalances on the SEC filing lag, and values each exact listed security on raw closing prices. Returns price return (dividends excluded), CAGR, and max drawdown for the clone and benchmark, plus price-return alpha. Usable captured split ratios restate closes onto one basis; an unusable ratio can exclude that listing's earlier closes."
    )]
    public Task<string> GetInstitutionCloneBacktest(
        [Description(
            "Institution name or SEC CIK (e.g., 'Berkshire Hathaway', '1067983', or zero-padded '0001067983'). Unique partials and verified aliases resolve; ambiguous partials return candidate CIKs."
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

                var resolution = await _holderRepository.ResolveNameOrCik(institution);
                if (resolution.Selected == null)
                {
                    if (resolution.Candidates.Count == 0)
                        return $"No match for '{institution}' in the tracked 13F filer set.";
                    return $"'{institution}' is ambiguous in the tracked 13F filer set: "
                        + FormatCandidates(resolution.Candidates)
                        + ". Pass the intended SEC CIK or an exact filed name.";
                }
                var holder = resolution.Selected.Holder;

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
                    outcome,
                    from,
                    explicitFrom.HasValue,
                    requestedYears,
                    windowYears
                );
                return Render(holder, outcome, notes);
            },
            "GetInstitutionCloneBacktest",
            $"institution: {institution}"
        );
    }

    // The annotation lines that keep the numbers honest: a clamped windowYears and a window
    // shortened by the available 13F history. Ambiguous filer names now fail before simulation.
    private static List<string> BuildNotes(
        CloneBacktestOutcome outcome,
        DateOnly requestedFrom,
        bool explicitWindow,
        int requestedYears,
        int clampedYears
    )
    {
        var notes = new List<string>();
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

    private static string FormatCandidates(
        IReadOnlyList<InstitutionalHolderSearchMatch> candidates
    ) =>
        string.Join(
            "; ",
            candidates.Select(c =>
                $"{MarkdownTable.EscapeCell(c.Holder.Name, "—")} (CIK {c.Holder.Cik}, latest {FormatOptionalDate(c.LatestReportDate)}, reported AUM {FormatOptionalDollars(c.ReportedAum)}, positions {FormatOptionalCount(c.PositionCount)})"
            )
        );

    private static string FormatOptionalDate(DateOnly? value) =>
        value == null ? "—" : McpFormat.Invariant(value.Value, "yyyy-MM-dd");

    private static string FormatOptionalDollars(long? value) =>
        value == null ? "—" : $"${McpFormat.WholeNumber(value.Value)}";

    private static string FormatOptionalCount(int? value) =>
        value == null ? "—" : McpFormat.WholeNumber(value.Value);

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
        output.AppendLine("| Strategy | Price return | Price CAGR | Max drawdown |");
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
            $"Alpha vs benchmark (price return): {FormatPercent(alpha)}. "
                + $"{result.Points.Count} daily points simulated."
        );
        output.AppendLine(
            "Raw closing prices are used, so dividends are excluded. Usable captured split ratios "
                + "restate closes onto one basis; an unusable ratio can exclude that listing's earlier closes."
        );

        // A clone is long-only, so a filer who expresses its thesis in options is only partly
        // tracked — and the answer above then describes the leftovers rather than the manager.
        // Stating that is not optional garnish: a model reading this tool has no other way to know,
        // and will otherwise report the number as the manager's performance.
        var coverage = result.Coverage;
        if (coverage is { QuartersMeasured: > 0 })
        {
            output.AppendLine();
            output.AppendLine(
                $"Coverage: the clone tracks {FormatShare(coverage.AverageLongPercent)} of "
                    + $"{holder.Name}'s reported 13F value on average across {coverage.QuartersMeasured} "
                    + $"quarter(s), and as little as {FormatShare(coverage.MinimumLongPercent)} in its "
                    + "thinnest quarter. The remainder is option positions, which a long-only clone "
                    + "cannot replicate and excludes."
            );
            if (!coverage.IsRepresentative)
            {
                output.AppendLine(
                    "WARNING: most of this filer's reported book is options, so the return above is "
                        + "the performance of the residual equity positions, NOT of this manager. Do "
                        + "not present it as their track record; a manager whose options thesis lost "
                        + "money can still show a large positive clone return."
                );
            }
        }

        if (result.TruncatedAt.HasValue)
        {
            output.AppendLine(
                $"Note: {holder.Name} has stopped filing, so the simulation ends on "
                    + $"{FormatDate(result.TruncatedAt.Value)} — the point its last reported "
                    + "portfolio went stale — rather than marking that portfolio forward to today."
            );
        }

        return output.ToString();
    }

    // Unsigned share of a whole, one decimal — coverage is never negative, so the signed format
    // used for returns would render a misleading leading "+".
    private static string FormatShare(decimal value) => McpFormat.Invariant(value, "0.0") + "%";

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
