using System.ComponentModel;
using System.Text;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.BusinessLogic.Extensions;
using Equibles.Holdings.BusinessLogic;
using Equibles.Holdings.BusinessLogic.Intelligence;
using Equibles.Holdings.BusinessLogic.Intelligence.Models;
using Equibles.Mcp;
using Equibles.Mcp.Helpers;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace Equibles.Holdings.Mcp.Tools;

[McpServerToolType]
public class HolderFlowTools
{
    private readonly HolderFlowReader _flowReader;
    private readonly McpToolRunner _runner;

    public HolderFlowTools(
        HolderFlowReader flowReader,
        ErrorManager errorManager,
        ILogger<HolderFlowTools> logger
    )
    {
        _flowReader = flowReader;
        _runner = new McpToolRunner(logger, errorManager.AsMcpErrorReporter());
    }

    [McpServerTool(Name = "GetHolderTypeFlows", Title = "13F Holder Flows", ReadOnly = true)]
    [Description(
        "Market-wide 13F money flows for a closed quarter by kind of filer: what each bucket bought and sold, net flow, net flow as % of its prior 13F book, and net buyers vs sellers. Buckets: holder type (regulatory records, never names), measured style, turnover, horizon, book size, client base, private funds or location. Optionally split by sector, with ETFs apart. Trades are priced at quarter-average prices; filers with no 13F the quarter before add no flow."
    )]
    public Task<string> GetHolderTypeFlows(
        [Description(
            "How to split the filers: type (holder type, default), style, turnover, horizon, aum (13F book size), client (client base), pf (private funds), country (filer location)"
        )]
            string dimension = "type",
        [Description(
            "Closed quarter-end report date in YYYY-MM-DD format (defaults to the newest computed quarter; a later date reads the newest computed quarter, an earlier off-quarter date the computed quarter before it)"
        )]
            string reportDate = null,
        [Description("none (default) or sector: split each bucket's flow by sector")]
            string groupBy = "none",
        [Description(
            "One bucket token of the dimension (e.g. adv, idx, high) to list that bucket's flow by sector; implies groupBy=sector"
        )]
            string bucket = null
    )
    {
        return _runner.Execute(
            async () =>
            {
                var (parsed, dimensionError) = HolderBucketTokens.ParseMixDimension(dimension);
                if (dimensionError != null)
                    return dimensionError;
                var mixDimension = parsed ?? HolderMixDimension.EntityType;
                var (grouping, groupingError) = HolderBucketTokens.ParseFlowGrouping(groupBy);
                if (groupingError != null)
                    return groupingError;
                if (!string.IsNullOrWhiteSpace(bucket))
                    grouping = HolderFlowGrouping.Sector;

                var quarters = await _flowReader.GetQuarters();
                if (quarters.Count == 0)
                    return "Holder flows are not computed yet: no closed 13F quarter has been measured.";
                var (quarter, note, dateError) = ResolveQuarter(quarters, reportDate);
                if (dateError != null)
                    return dateError;

                var flows = await _flowReader.ReadQuarter(quarter, mixDimension, grouping);
                if (!string.IsNullOrWhiteSpace(bucket))
                {
                    var selected = flows.Buckets.FirstOrDefault(row =>
                        string.Equals(row.Token, bucket.Trim(), StringComparison.OrdinalIgnoreCase)
                    );
                    if (selected == null)
                        return $"Unknown bucket '{bucket.Trim()}' for {HolderBucketTokens.Label(mixDimension).ToLowerInvariant()} in {FormatDate(flows.ReportDate)}. Use one of: {string.Join(", ", flows.Buckets.Select(row => row.Token))}.";
                    return Render(flows, note, selected);
                }
                return Render(flows, note, null);
            },
            "GetHolderTypeFlows",
            $"dimension: {dimension}, reportDate: {reportDate}, groupBy: {groupBy}, bucket: {bucket}"
        );
    }

    // The requested quarter when computed; a later date reads the newest computed quarter and an
    // earlier one the computed quarter on or before it, each with a note saying so.
    private static (HolderFlowQuarterState Quarter, string Note, string Error) ResolveQuarter(
        IReadOnlyList<HolderFlowQuarterState> quarters,
        string reportDate
    )
    {
        var newest = quarters[^1];
        if (string.IsNullOrWhiteSpace(reportDate))
            return (newest, null, null);
        if (!McpOutput.TryParseDate(reportDate, out var parsed))
            return (
                null,
                null,
                McpOutput.InvalidArgument("reportDate", reportDate, "a date in YYYY-MM-DD format")
            );
        var requested = DateOnly.FromDateTime(parsed);
        if (requested > newest.ReportDate)
            return (
                newest,
                $"{FormatDate(requested)} is not computed yet (a quarter is measured once its 13F filing window closes); showing {FormatDate(newest.ReportDate)}.",
                null
            );
        var match = quarters.LastOrDefault(quarter => quarter.ReportDate <= requested);
        if (match == null)
            return (
                null,
                null,
                $"No holder flows before {FormatDate(quarters[0].ReportDate)}; computed quarters run {FormatDate(quarters[0].ReportDate)} to {FormatDate(newest.ReportDate)}."
            );
        return match.ReportDate == requested
            ? (match, null, null)
            : (
                match,
                $"{FormatDate(requested)} is not a computed quarter end; showing {FormatDate(match.ReportDate)}.",
                null
            );
    }

    private static string Render(HolderFlowQuarter flows, string note, HolderFlowBucket selected)
    {
        var dimensionLabel = HolderBucketTokens.Label(flows.Dimension);
        var previous = HoldingsCorpusCoverage.PreviousQuarterEnd(flows.ReportDate);
        var period = $"quarter ended {FormatDate(flows.ReportDate)} vs {FormatDate(previous)}";
        var bySector = flows.Grouping == HolderFlowGrouping.Sector && flows.SectorSplitAvailable;

        StringBuilder result;
        if (selected != null && bySector)
            result = RenderBucketSectors(flows, selected, period);
        else if (bySector)
            result = RenderSectorMatrix(flows, dimensionLabel, period);
        else
            result = RenderBuckets(flows, dimensionLabel, period);

        result.AppendLine();
        if (note != null)
            result.AppendLine(note);
        if (flows.Grouping == HolderFlowGrouping.Sector && !flows.SectorSplitAvailable)
            result.AppendLine(
                $"The sector split of {FormatDate(flows.ReportDate)} is not computed yet; it fills in as the quarter is recomputed. Showing totals by {dimensionLabel.ToLowerInvariant()}."
            );
        var total = flows.Total;
        var uncompared = total.Filers - total.ComparedFilers;
        result.AppendLine(
            $"{FormatDate(flows.ReportDate)}: {McpFormat.WholeNumber(total.ComparedFilers)} of {McpFormat.WholeNumber(total.Filers)} filers had a 13F the quarter before and are compared; {McpFormat.WholeNumber(uncompared)} add no flow. {McpFormat.WholeNumber(total.UnpricedTrades)} trades had no price and are left out. Computed {FormatDate(DateOnly.FromDateTime(flows.ComputedAt))}."
        );
        result.AppendLine();
        result.AppendLine(
            $"_Bought and sold are changes in each filer's 13F common-share positions (options and principal amounts left out), priced at the security's average close over the quarter, else its quarter-end value per share (an exit at the quarter before's); a share change from a corporate action reads as a trade. Net % of prior book divides net flow by the compared filers' 13F equity value the quarter before. Holder type, client base and private funds are each filer's current regulatory profile; style, turnover, horizon and book size are measured from its own 13F book that quarter. A security's sector is its issuer's when the quarter was computed; exchange-traded products are the US listings in the exchange-traded product reference. Other dimensions: {HolderBucketTokens.MixDimensionTokenList}._"
        );
        return result.ToString();
    }

    private static StringBuilder RenderBuckets(
        HolderFlowQuarter flows,
        string dimensionLabel,
        string period
    )
    {
        var result = MarkdownTable.Start(
            $"13F holder flows by {dimensionLabel.ToLowerInvariant()}, {period}",
            $"| {dimensionLabel} | Bought | Sold | Net flow | Net % of prior book | Filers compared | Net buyers / sellers |",
            "|---|---|---|---|---|---|---|"
        );
        foreach (var row in flows.Buckets.Append(flows.Total))
            result.AppendLine(
                $"| {MarkdownTable.EscapeCell(row.Label, "—")} | {Usd(row.BuyValue)} | {Usd(row.SellValue)} | {SignedUsd(row.NetValue)} | {SignedPercent(row.NetPercentOfPriorBook)} | {McpFormat.WholeNumber(row.ComparedFilers)} of {McpFormat.WholeNumber(row.Filers)} | {McpFormat.WholeNumber(row.NetBuyers)} / {McpFormat.WholeNumber(row.NetSellers)} |"
            );
        return result;
    }

    // Sectors down, buckets across: each cell is the bucket's net flow into the sector.
    private static StringBuilder RenderSectorMatrix(
        HolderFlowQuarter flows,
        string dimensionLabel,
        string period
    )
    {
        var columns = flows.Buckets.Append(flows.Total).ToList();
        var result = MarkdownTable.Start(
            $"13F net flow by sector and {dimensionLabel.ToLowerInvariant()}, {period}",
            $"| Sector | {string.Join(" | ", columns.Select(column => MarkdownTable.EscapeCell(column.Label, "—")))} |",
            $"|---|{string.Join("|", columns.Select(_ => "---"))}|"
        );
        foreach (var sector in flows.Total.Sectors)
            result.AppendLine(
                $"| {MarkdownTable.EscapeCell(sector.Name, "—")} | {string.Join(" | ", columns.Select(column => NetIn(column, sector)))} |"
            );
        result.AppendLine(
            $"| All sectors | {string.Join(" | ", columns.Select(column => SignedUsd(column.NetValue)))} |"
        );
        return result;
    }

    private static StringBuilder RenderBucketSectors(
        HolderFlowQuarter flows,
        HolderFlowBucket bucket,
        string period
    )
    {
        var result = MarkdownTable.Start(
            $"13F flows of {bucket.Label} by sector, {period}",
            "| Sector | Bought | Sold | Net flow |",
            "|---|---|---|---|"
        );
        foreach (var sector in bucket.Sectors)
            result.AppendLine(
                $"| {MarkdownTable.EscapeCell(sector.Name, "—")} | {Usd(sector.BuyValue)} | {Usd(sector.SellValue)} | {SignedUsd(sector.NetValue)} |"
            );
        result.AppendLine(
            $"| All sectors | {Usd(bucket.BuyValue)} | {Usd(bucket.SellValue)} | {SignedUsd(bucket.NetValue)} |"
        );
        result.AppendLine();
        result.AppendLine(
            $"{bucket.Label}: {SignedPercent(bucket.NetPercentOfPriorBook)} of its prior 13F book, {McpFormat.WholeNumber(bucket.NetBuyers)} net buyers and {McpFormat.WholeNumber(bucket.NetSellers)} net sellers among {McpFormat.WholeNumber(bucket.ComparedFilers)} compared filers."
        );
        return result;
    }

    private static string NetIn(HolderFlowBucket bucket, HolderFlowSectorAmount sector) =>
        bucket.Sectors.FirstOrDefault(row =>
            row.Group == sector.Group && row.SectorId == sector.SectorId
        )
            is { } amount
            ? SignedUsd(amount.NetValue)
            : "—";

    private static string Usd(decimal value) => McpFormat.CompactUsd((double)value);

    private static string SignedUsd(decimal value) =>
        value switch
        {
            > 0 => "+" + Usd(value),
            < 0 => "-" + Usd(-value),
            _ => Usd(0m),
        };

    private static string SignedPercent(decimal? value) =>
        value is { } percent ? $"{McpFormat.Invariant(percent, "+0.0;-0.0;0.0")}%" : "—";

    private static string FormatDate(DateOnly date) => McpFormat.Invariant(date, "yyyy-MM-dd");
}
