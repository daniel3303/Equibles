using System.ComponentModel;
using System.Globalization;
using System.Text;
using Equibles.CommonStocks.Data.Helpers;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.CommonStocks.Repositories.Extensions;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.BusinessLogic.Extensions;
using Equibles.Holdings.BusinessLogic;
using Equibles.Holdings.BusinessLogic.Signals;
using Equibles.Holdings.BusinessLogic.Signals.Models;
using Equibles.Holdings.Data.Models;
using Equibles.Mcp;
using Equibles.Mcp.Helpers;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace Equibles.Holdings.Mcp.Tools;

/// <summary>
/// Per-stock holder signals measured across every 13F book of a closed quarter (conviction,
/// crowding, tenure, concentration, estimated cost, holder-overlap peers) and shareholder
/// targeting from those peers.
/// </summary>
[McpServerToolType]
public class HolderSignalsTools
{
    private const int DefaultPeers = 10;
    private const string Dash = "—";

    private static readonly string[] TargetSorts = ["peervalue", "peerweight"];

    private readonly EquityIssuerRepository _commonStockRepository;
    private readonly HolderSignalsReader _signalsReader;
    private readonly HolderTargetsReader _targetsReader;
    private readonly McpToolRunner _runner;

    public HolderSignalsTools(
        EquityIssuerRepository commonStockRepository,
        HolderSignalsReader signalsReader,
        HolderTargetsReader targetsReader,
        ErrorManager errorManager,
        ILogger<HolderSignalsTools> logger
    )
    {
        _commonStockRepository = commonStockRepository;
        _signalsReader = signalsReader;
        _targetsReader = targetsReader;
        _runner = new McpToolRunner(logger, errorManager.AsMcpErrorReporter());
    }

    [McpServerTool(Name = "GetOwnershipSignals", Title = "13F Ownership Signals", ReadOnly = true)]
    [Description(
        "Who holds a stock and how, measured across every 13F book of the newest closed quarter, beside the quarter before: conviction (filers holding it among their ten largest positions, its weight in their books), crowding (shares held by high-turnover filers and private-fund advisers as % of shares outstanding, with its percentile), institutional ownership of the estimated free float, holder concentration (Herfindahl, largest ten holders), how long holders have held it, the holders' estimated average cost per share and the share of 13F value held at a loss, active holders' like-for-like change, top-scored managers' net buying, Schedule 13D and 13G filers on record, holder-overlap peers (stocks whose active holders overlap most), and the filers with the highest weight in it. Cost is an estimate from quarter average prices, never a filed figure."
    )]
    public Task<string> GetOwnershipSignals(
        [Description("Listed security ticker (e.g., NVDA)")] string ticker,
        [Description(
            "Highest-conviction holders to list, by the stock's weight in their 13F book (default 10, clamped to 0-50)"
        )]
            int maxHolders = HolderSignalsReader.DefaultConvictionHolders,
        [Description("Holder-overlap peers to list (default 10, clamped to 0-20)")]
            int maxPeers = DefaultPeers
    )
    {
        return _runner.Execute(
            async () =>
            {
                var (stock, stockError) = await _commonStockRepository.ResolveByTicker(ticker);
                if (stockError != null)
                    return stockError;
                var view = await _signalsReader.Read(stock, ticker, maxHolders);
                var label = Label(stock, view.ListedTicker);
                return view.Unavailable != null
                    ? $"{label}: {view.Unavailable}"
                    : RenderSignals(label, view, Math.Clamp(maxPeers, 0, 20));
            },
            "GetOwnershipSignals",
            $"ticker: {ticker}, maxHolders: {maxHolders}, maxPeers: {maxPeers}"
        );
    }

    [McpServerTool(Name = "GetHolderTargets", Title = "Shareholder Targets", ReadOnly = true)]
    [Description(
        "Shareholder targeting for a stock: the 13F filers of the newest closed quarter that hold its holder-overlap peers (stocks whose active holders overlap most) but hold no class of the stock itself, ranked by the money they have in those peers. A stock with no overlap peers falls back to its sector's largest 13F holdings. Each target shows its 13F book, the peers' share of it, the peers it holds most of, its measured style and its holder type from regulatory records."
    )]
    public Task<string> GetHolderTargets(
        [Description("Listed security ticker (e.g., NVDA)")] string ticker,
        [Description(
            "Rank by: 'peerValue' (default, dollars held in the peers) or 'peerWeight' (the peers' share of the filer's 13F book, then book size)"
        )]
            string sort = "peerValue",
        [Description("Maximum number of targets to return (default 25, clamped to 1-500)")]
            int maxResults = HolderTargetsReader.DefaultMaxResults
    )
    {
        return _runner.Execute(
            async () =>
            {
                var normalized = (sort ?? "peervalue").Trim().ToLowerInvariant();
                if (!TargetSorts.Contains(normalized))
                    return McpOutput.InvalidArgument("sort", sort, "peerValue, peerWeight");
                var (stock, stockError) = await _commonStockRepository.ResolveByTicker(ticker);
                if (stockError != null)
                    return stockError;
                var targets = await _targetsReader.Read(
                    stock,
                    ticker,
                    normalized == "peerweight"
                        ? StockHolderTargetSort.PeerWeight
                        : StockHolderTargetSort.PeerValue,
                    McpLimit.Clamp(maxResults)
                );
                var label = Label(stock, HolderSignalsReader.SignalsListing(stock, ticker));
                return targets.Unavailable != null
                    ? $"{label}: {targets.Unavailable}"
                    : RenderTargets(label, targets);
            },
            "GetHolderTargets",
            $"ticker: {ticker}, sort: {sort}, maxResults: {maxResults}"
        );
    }

    internal static string RenderSignals(string label, StockHolderSignalsView view, int maxPeers)
    {
        var current = view.Current;
        var previous = view.Previous;
        var result = new StringBuilder();
        result.AppendLine(
            $"13F ownership signals for {label}, quarter ended {FormatDate(current.ReportDate)}"
                + (previous != null ? $" (vs {FormatDate(previous.ReportDate)})" : "")
        );
        result.AppendLine();
        result.AppendLine(
            previous != null
                ? $"| Signal | {FormatDate(current.ReportDate)} | {FormatDate(previous.ReportDate)} |"
                : $"| Signal | {FormatDate(current.ReportDate)} |"
        );
        result.AppendLine(previous != null ? "|---|---|---|" : "|---|---|");
        void Row(string name, Func<StockQuarterlyHolderSignals, string> cell) =>
            result.AppendLine(
                previous != null
                    ? $"| {name} | {cell(current)} | {cell(previous)} |"
                    : $"| {name} | {cell(current)} |"
            );

        Row("13F filers", row => McpFormat.WholeNumber(row.Filers));
        Row("13F shares", row => McpFormat.WholeNumber(row.Shares));
        Row("13F value ($M)", row => Millions(row.Value));
        Row(
            "Conviction holders (among a filer's 10 largest positions)",
            row =>
                $"{McpFormat.WholeNumber(row.ConvictionHolders)} ({Percent(StockHolderSignalsMath.ConvictionShare(row))} of filers)"
        );
        Row(
            "Filers with 5% or more of their book in it",
            row => McpFormat.WholeNumber(row.HoldersAboveFivePercent)
        );
        Row(
            "Weight in holders' books, average / median",
            row => $"{Percent(row.AverageWeight)} / {Percent(row.MedianWeight)}"
        );
        Row(
            "Crowding: high-turnover and private-fund adviser shares, % of shares outstanding",
            row =>
                StockHolderSignalsMath.Crowding(row) is { } crowding
                    ? $"{Percent(crowding)} ({PercentileText(row.CrowdingPercentile)}, {McpFormat.WholeNumber(row.CrowdedHolders)} filers)"
                    : Dash
        );
        Row(
            "Private-fund advisers' shares, % of shares outstanding",
            row => Percent(StockHolderSignalsMath.PrivateFundOwnership(row))
        );
        Row(
            "13F shares, % of shares outstanding",
            row => Percent(StockHolderSignalsMath.InstitutionalShareOfOutstanding(row))
        );
        Row(
            "13F shares, % of estimated free float",
            row => Percent(StockHolderSignalsMath.InstitutionalShareOfFloat(row))
        );
        Row(
            "Holder concentration (Herfindahl, 0 to 1)",
            row => row.Herfindahl is { } hhi ? McpFormat.Invariant(hhi, "F4") : Dash
        );
        Row("Largest 10 holders' share of 13F shares", row => Percent(row.TopTenHolderShare));
        Row(
            "Value-weighted holding period (quarters)",
            row => row.ValueWeightedTenureQuarters is { } q ? McpFormat.Invariant(q, "F1") : Dash
        );
        Row(
            "Held under 1 year / 1 to 3 years / 3 years or more / since coverage start",
            TenureText
        );
        Row(
            "Estimated holder cost per share",
            row =>
                row.EstimatedCostPerShare is { } cost
                    ? $"${McpFormat.Price(cost)} (covers {Percent(StockHolderSignalsMath.CostCoverage(row))} of 13F shares)"
                    : Dash
        );
        Row(
            "Quarter-end price implied by 13F values",
            row =>
                StockHolderSignalsMath.QuarterEndPrice(row) is { } price
                    ? $"${McpFormat.Price(price)}"
                    : Dash
        );
        Row("Price vs estimated cost", row => SignedPercent(StockHolderSignalsMath.CostGap(row)));
        Row("Estimated 13F value held at a loss", row => Percent(row.UnderwaterValueShare));
        Row(
            "Active holders' like-for-like change",
            row => SignedPercent(StockHolderSignalsMath.ActiveLikeForLikeChange(row))
        );
        Row(
            "Top-fifth FundScore managers' net buying ($M)",
            row =>
                row.TopScoreNetBuyValue is { } net
                    ? $"{SignedMillions(net)} ({McpFormat.WholeNumber(row.TopScoreBuyers ?? 0)} buying, {McpFormat.WholeNumber(row.TopScoreSellers ?? 0)} selling)"
                    : Dash
        );
        Row(
            "Schedule 13D / 13G filers with a stake on record",
            row =>
                $"{McpFormat.WholeNumber(row.Schedule13DFilers)} / {McpFormat.WholeNumber(row.Schedule13GFilers)}"
        );

        result.AppendLine();
        var notes = new List<string>
        {
            "Share counts are as filed for each quarter. Cost per share is ESTIMATED: each buy is costed at the quarter's average price, sells keep the cost, and positions held before a filer's comparable history or bought at an untrusted price are left out (coverage shown).",
        };
        if (StockHolderSignalsMath.OutstandingUnavailableReason(current) is { } reason)
            notes.Add($"No % of shares outstanding: {reason}.");
        else
        {
            var floatNote = current.SharesOutstandingAsOf is { } asOf
                ? $"Shares outstanding {McpFormat.WholeNumber(current.SharesOutstanding ?? 0)} from the cover page dated {FormatDate(asOf)}"
                : "Shares outstanding from the cover page";
            notes.Add(
                current.InsiderShares is { } insiders
                    ? $"{floatNote}; estimated free float subtracts {McpFormat.WholeNumber(insiders)} insider shares (Forms 3, 4, 5 within two years, 13F filers excluded)."
                    : $"{floatNote}."
            );
        }
        if (current.TopScoreNetBuyValue == null)
            notes.Add(
                "Top-scored managers' buying is measured for the newest closed quarter only."
            );
        if (StockHolderSignalsMath.InstitutionalShareOfFloat(current) is > 1m)
            notes.Add(
                "13F shares exceed the estimated free float: filers can report shares lent out or held for clients more than once."
            );
        foreach (var note in notes)
            result.AppendLine($"_{note}_");

        if (maxPeers > 0 && view.Peers.Count > 0)
        {
            result.AppendLine();
            result.AppendLine(
                $"Holder-overlap peers ({FormatDate(view.PeersAsOf.Value)}): stocks whose concentrated and diversified active holders overlap most"
            );
            result.AppendLine();
            result.AppendNumberedTable(
                "| # | Ticker | Company | Shared active holders | Similarity |",
                "|---|--------|---------|-----------------------|------------|",
                view.Peers.Take(maxPeers).ToList(),
                (rank, peer) =>
                    $"| {rank} | {peer.Ticker} | {MarkdownTable.EscapeCell(peer.Name)} | {McpFormat.WholeNumber(peer.SharedHolders)} | {McpFormat.Invariant(peer.Similarity, "F3")} |"
            );
        }

        if (view.ConvictionHolders.Count > 0)
        {
            result.AppendLine();
            result.AppendLine(
                $"Highest-conviction 13F holders ({FormatDate(current.ReportDate)}), by weight in their book"
            );
            result.AppendLine();
            result.AppendNumberedTable(
                "| # | Holder | CIK | Type | Style | Weight | Value ($M) | 13F book ($M) |",
                "|---|--------|-----|------|-------|--------|------------|---------------|",
                view.ConvictionHolders.ToList(),
                (rank, holder) =>
                    $"| {rank} | {MarkdownTable.EscapeCell(holder.Name)} | {holder.Cik} | {HolderBucketTokens.Label(holder.EntityType)} | {HolderBucketTokens.StyleLabel(holder.StyleBand)} | {Percent(holder.Weight)} | {Millions(holder.Value)} | {Millions(holder.EquityAum)} |"
            );
            result.AppendLine();
            result.AppendLine(
                "_Type comes from regulatory records, never the name; style is measured from the filed book._"
            );
        }
        return result.ToString();
    }

    internal static string RenderTargets(string label, StockHolderTargets targets)
    {
        var result = new StringBuilder();
        var basis = targets.SectorFallback
            ? $"the largest 13F holdings in its sector ({targets.SectorName}), since it has no holder-overlap peers"
            : "its holder-overlap peers";
        result.AppendLine(
            $"Shareholder targets for {label}, 13F quarter ended {FormatDate(targets.ReportDate.Value)}"
        );
        result.AppendLine(
            $"13F filers that hold {basis} but no class of {label}: {McpFormat.WholeNumber(targets.TotalTargets)} found, ranked by "
                + (
                    targets.Sort == StockHolderTargetSort.PeerWeight
                        ? "the peers' share of the filer's book."
                        : "dollars held in the peers."
                )
        );
        result.AppendLine("Peers: " + string.Join(", ", targets.Peers.Select(peer => peer.Ticker)));
        result.AppendLine();
        result.AppendNumberedTable(
            "| # | Holder | CIK | Type | Style | In peers ($M) | Peers' share of book | Peers held | Largest peer positions | 13F book ($M) |",
            "|---|--------|-----|------|-------|---------------|----------------------|------------|------------------------|---------------|",
            targets.Targets.ToList(),
            (rank, target) =>
                $"| {rank} | {MarkdownTable.EscapeCell(target.Name)} | {target.Cik} | {HolderBucketTokens.Label(target.EntityType)} | {HolderBucketTokens.StyleLabel(target.StyleBand)} | {Millions(target.PeerValue)} | {Percent(target.PeerWeight)} | {McpFormat.WholeNumber(target.PeersHeld)} | {string.Join(", ", target.TopPeers)} | {Millions(target.EquityAum)} |"
        );
        if (targets.Targets.Count < targets.TotalTargets)
            result.AppendLine(
                $"_Showing the first {targets.Targets.Count} of {McpFormat.WholeNumber(targets.TotalTargets)} targets._"
            );
        result.AppendLine();
        result.AppendLine(
            "_A filer holding any class of the stock that quarter is left out. Type comes from regulatory records, never the name; style is measured from the filed book._"
        );
        return result.ToString();
    }

    private static string TenureText(StockQuarterlyHolderSignals row) =>
        StockHolderSignalsMath.TenureMix(row) is { } mix
            ? $"{Percent(mix.Short)} / {Percent(mix.Medium)} / {Percent(mix.Long)} / {Percent(mix.Censored)}"
            : Dash;

    private static string PercentileText(decimal? percentile) =>
        percentile is { } value
            ? $"{McpFormat.Invariant(Math.Round(value), "F0")}th percentile"
            : "no percentile";

    private static string Label(EquityIssuer stock, string listedTicker) =>
        listedTicker == null
            ? $"{stock.Name} ({stock.Presentation?.Listing?.Ticker})"
            : $"{stock.Name} ({listedTicker})";

    private static string FormatDate(DateOnly date) =>
        date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string Percent(decimal? fraction) =>
        fraction is { } value ? McpFormat.Invariant(value * 100, "F1") + "%" : Dash;

    private static string SignedPercent(decimal? fraction) =>
        fraction is { } value
            ? (value > 0 ? "+" : "") + McpFormat.Invariant(value * 100, "F1") + "%"
            : Dash;

    private static string Millions(long value) => McpFormat.Invariant(value / 1_000_000m, "N1");

    private static string SignedMillions(decimal value) =>
        (value > 0 ? "+" : "") + McpFormat.Invariant(value / 1_000_000m, "N1");
}
