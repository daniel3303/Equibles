using System.ComponentModel;
using System.Text;
using Equibles.CommonStocks.Data.Helpers;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.CommonStocks.Repositories.Extensions;
using Equibles.Core.Extensions;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.BusinessLogic.Extensions;
using Equibles.Holdings.BusinessLogic;
using Equibles.Holdings.BusinessLogic.Ownership;
using Equibles.Holdings.BusinessLogic.Ownership.Models;
using Equibles.Holdings.Data.Models;
using Equibles.Mcp;
using Equibles.Mcp.Helpers;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace Equibles.Holdings.Mcp.Tools;

/// <summary>
/// The unified holder list (every SEC disclosure of a stake in one table), the ownership summary
/// built from it, and the per-stock Schedule 13D and 13G record.
/// </summary>
[McpServerToolType]
public class StockOwnershipTools
{
    // Nested rows shown under each default-view holder; view=all lists every one.
    private const int ChildrenShown = 5;

    private const int DefaultMaxResults = 25;

    private static readonly string[] Sources =
    [
        "all",
        "13f",
        "13dg",
        "13d",
        "13g",
        "insider",
        "fund",
    ];
    private static readonly string[] Views = ["default", "all", "ultimate"];
    private static readonly string[] SummaryViews = ["default", "ultimate"];

    private const string HolderTypesParameter =
        "Holder types to keep, as an array of tokens: adv, era, ia, bank, ins, bd, fund, plan, nonus, hc, unk (insiders and holders without a deciding record read unk), e.g. [\"bank\"]. Types come from regulatory records, never names. Omit for all.";

    private readonly EquityIssuerRepository _commonStockRepository;
    private readonly StockOwnershipReader _ownershipReader;
    private readonly StockBeneficialOwnershipReader _beneficialOwnershipReader;
    private readonly McpToolRunner _runner;

    public StockOwnershipTools(
        EquityIssuerRepository commonStockRepository,
        StockOwnershipReader ownershipReader,
        StockBeneficialOwnershipReader beneficialOwnershipReader,
        ErrorManager errorManager,
        ILogger<StockOwnershipTools> logger
    )
    {
        _commonStockRepository = commonStockRepository;
        _ownershipReader = ownershipReader;
        _beneficialOwnershipReader = beneficialOwnershipReader;
        _runner = new McpToolRunner(logger, errorManager.AsMcpErrorReporter());
    }

    [McpServerTool(Name = "GetAllHolders", Title = "All Holders of a Stock", ReadOnly = true)]
    [Description(
        "Get every disclosed holder of a stock or ETF listing in one table: 13F institutions, Schedule 13D and 13G filers, insiders (Forms 3, 4, 5) and registered funds (N-PORT). The default view shows one row per holder from its freshest filing, with its other filings and the 13D/G, insider and fund disclosures its 13F report contains nested under it, matched by CIK and SEC reporting links, never by name. Shares are restated onto today's split basis, change is against the same holder's previous filing in the same source, and % Out is of shares outstanding. Funds are never summed: their shares sit inside their advisers' 13F reports. view=all lists every row flat, stale ones included; view=ultimate groups the summed holders under their ultimate parent. Use GetOwnershipSummary for the totals."
    )]
    public Task<string> GetAllHolders(
        [Description("Listed security ticker (e.g., NVDA, VOO)")] string ticker,
        [Description(
            "Source to list: all (default), 13f, 13dg (13D and 13G), 13d, 13g, insider or fund. A single source lists its rows flat, stale ones flagged."
        )]
            string source = "all",
        [Description(
            "default (one row per holder, others nested), all (every row flat, stale included) or ultimate (summed holders grouped by ultimate parent)"
        )]
            string view = "default",
        [Description(HolderTypesParameter)] string[] holderTypes = null,
        [Description(
            "Maximum number of rows (holders, or parents for view=ultimate) to return (default: 25, clamped to 1-500)"
        )]
            int maxResults = DefaultMaxResults,
        [Description("Number of ranked rows to skip before returning results (default: 0)")]
            int offset = 0
    )
    {
        return _runner.Execute(
            async () =>
            {
                source = (source ?? "all").Trim().ToLowerInvariant();
                if (!Sources.Contains(source))
                    return McpOutput.InvalidArgument("source", source, string.Join(", ", Sources));
                view = (view ?? "default").Trim().ToLowerInvariant();
                if (!Views.Contains(view))
                    return McpOutput.InvalidArgument("view", view, string.Join(", ", Views));
                var (typeFilter, typeError) = HolderBucketTokens.ParseEntityTypes(holderTypes);
                if (typeError != null)
                    return typeError;
                if (view == "ultimate" && (source != "all" || typeFilter != null))
                    return "view=ultimate groups every summed holder, so it takes no source or holderTypes filter.";

                var (stock, stockError) = await _commonStockRepository.ResolveByTicker(ticker);
                if (stockError != null)
                    return stockError;
                maxResults = McpLimit.Clamp(maxResults);
                offset = McpLimit.ClampOffset(offset);
                var cachedEnough =
                    view == "default"
                    && source == "all"
                    && typeFilter == null
                    && offset + maxResults <= StockOwnershipReader.CachedDefaultRows;
                var read = await _ownershipReader.Read(stock, ticker, complete: !cachedEnough);
                var label = ListingLabel(stock, read.ListedTicker);
                if (
                    read.Summary.Institutional.Holders == 0
                    && read.DefaultRowCount == 0
                    && read.Rows.Count == 0
                )
                    return $"No holder disclosures (13F, 13D, 13G, Forms 3 to 5 or N-PORT) found for {label}.";

                return view == "ultimate" ? RenderUltimate(read, label, maxResults, offset)
                    : view == "default" && source == "all"
                        ? RenderDefault(read, label, typeFilter, maxResults, offset)
                    : RenderFlat(read, label, source, typeFilter, maxResults, offset);
            },
            "GetAllHolders",
            $"ticker: {ticker}, source: {source}, view: {view}, holderTypes: {string.Join(",", holderTypes ?? [])}, maxResults: {maxResults}, offset: {offset}"
        );
    }

    [McpServerTool(
        Name = "GetOwnershipSummary",
        Title = "Stock Ownership Summary",
        ReadOnly = true
    )]
    [Description(
        "Get who owns a stock, as shares and % of shares outstanding: 13F institutions, insiders (Forms 3, 4, 5, single-class issuers only), 13D/13G blocks no 13F report contains, and the remaining float, flagged rather than clipped when negative. Registered funds are shown but never summed, since their shares sit inside their advisers' 13F reports. Each holder counts once: insiders who file 13Fs, holders a 13F filer reports and 13D co-filers of 13F filers are left out of the other lines, by CIK and SEC reporting links, never by name. Also splits the 13F shares by holder type, style and location. view=ultimate adds the largest ultimate parents."
    )]
    public Task<string> GetOwnershipSummary(
        [Description("Listed security ticker (e.g., NVDA, VOO)")] string ticker,
        [Description("default, or ultimate to add the largest ultimate parents")]
            string view = "default"
    )
    {
        return _runner.Execute(
            async () =>
            {
                view = (view ?? "default").Trim().ToLowerInvariant();
                if (!SummaryViews.Contains(view))
                    return McpOutput.InvalidArgument("view", view, string.Join(", ", SummaryViews));
                var (stock, stockError) = await _commonStockRepository.ResolveByTicker(ticker);
                if (stockError != null)
                    return stockError;
                var read = await _ownershipReader.Read(stock, ticker, complete: view == "ultimate");
                var label = ListingLabel(stock, read.ListedTicker);
                if (read.Summary.Institutional.Holders == 0 && read.DefaultRowCount == 0)
                    return $"No holder disclosures (13F, 13D, 13G, Forms 3 to 5 or N-PORT) found for {label}.";
                return RenderSummary(read, label, view == "ultimate");
            },
            "GetOwnershipSummary",
            $"ticker: {ticker}, view: {view}"
        );
    }

    [McpServerTool(
        Name = "GetBeneficialOwnership",
        Title = "Stock 13D and 13G Filers",
        ReadOnly = true
    )]
    [Description(
        "Get the Schedule 13D and 13G beneficial-ownership record of a stock or ETF listing: each filer's latest 13D or 13G, its event date, the block's shares (restated onto today's split basis) and percent of class, the change since the filer's previous 13D or 13G, and every reporting person on the cover pages with their own amount, percent and sole or shared voting and dispositive power. Reporting persons on one filing each claim the same block, so never add them together. Largest block first."
    )]
    public Task<string> GetBeneficialOwnership(
        [Description("Listed security ticker (e.g., NVDA, BRK-B)")] string ticker,
        [Description("Maximum number of filers to return (default: 20, clamped to 1-500)")]
            int maxResults = 20,
        [Description("Number of filers to skip before returning results (default: 0)")]
            int offset = 0
    )
    {
        return _runner.Execute(
            async () =>
            {
                var (stock, stockError) = await _commonStockRepository.ResolveByTicker(ticker);
                if (stockError != null)
                    return stockError;
                var listedTicker =
                    SecondaryTickerPolicy.ResolveListedTicker(stock, ticker)
                    ?? stock.Presentation.Listing.Ticker;
                var label = ListingLabel(stock, listedTicker);
                var blocks = await _beneficialOwnershipReader.Read(stock, ticker);
                if (blocks.Count == 0)
                    return $"No Schedule 13D or 13G filings found for {label}.";
                maxResults = McpLimit.Clamp(maxResults);
                offset = McpLimit.ClampOffset(offset);
                var page = blocks.Skip(offset).Take(maxResults).ToList();
                if (page.Count == 0)
                    return McpOutput.PagedTruncationNote(0, blocks.Count, offset);
                return RenderBeneficialOwnership(label, blocks.Count, page, offset);
            },
            "GetBeneficialOwnership",
            $"ticker: {ticker}, maxResults: {maxResults}, offset: {offset}"
        );
    }

    private static string RenderDefault(
        StockOwnershipRead read,
        string label,
        IReadOnlySet<HolderEntityType> typeFilter,
        int maxResults,
        int offset
    )
    {
        IReadOnlyList<StockHolderRow> tops = read.DefaultRows;
        var total = read.DefaultRowCount;
        if (typeFilter != null)
        {
            tops = tops.Where(row =>
                    typeFilter.Contains(row.EntityType ?? HolderEntityType.Unknown)
                )
                .ToList();
            total = tops.Count;
        }
        var page = tops.Skip(offset).Take(maxResults).ToList();
        if (page.Count == 0)
            return offset > 0
                ? McpOutput.PagedTruncationNote(0, total, offset)
                : $"No holders of {label} with holder type {HolderBucketTokens.Describe(typeFilter)}.";

        var result = Start(
            read,
            label,
            $"{McpFormat.WholeNumber(total)} holders in the default view"
        );
        var hidden = 0;
        for (var index = 0; index < page.Count; index++)
        {
            var top = page[index];
            var rank = McpFormat.Invariant(offset + index + 1, "0");
            result.AppendLine(Row(rank, top, nested: false));
            var shown = top.Children.Take(ChildrenShown).ToList();
            for (var child = 0; child < shown.Count; child++)
                result.AppendLine(Row($"{rank}.{child + 1}", shown[child], nested: true));
            hidden += Math.Max(0, top.ChildCount - shown.Count);
        }
        result.AppendLine();
        if (hidden > 0)
            result.AppendLine(
                $"_{McpFormat.WholeNumber(hidden)} more nested rows are not shown; view=all or a source filter lists every row._"
            );
        AppendRowNotes(result, read);
        result.Append(McpOutput.PagedTruncationNote(page.Count, total, offset));
        return result.ToString().TrimEnd();
    }

    private static string RenderFlat(
        StockOwnershipRead read,
        string label,
        string source,
        IReadOnlySet<HolderEntityType> typeFilter,
        int maxResults,
        int offset
    )
    {
        var rows = read
            .Rows.Where(row => MatchesSource(row.Source, source))
            .Where(row =>
                typeFilter == null
                || typeFilter.Contains(row.EntityType ?? HolderEntityType.Unknown)
            )
            .ToList();
        var page = rows.Skip(offset).Take(maxResults).ToList();
        if (page.Count == 0)
            return offset > 0
                ? McpOutput.PagedTruncationNote(0, rows.Count, offset)
                : $"No {SourceLabel(source)} rows for {label}.";

        var result = Start(
            read,
            label,
            $"{McpFormat.WholeNumber(rows.Count)} {SourceLabel(source)} rows, stale ones included"
        );
        for (var index = 0; index < page.Count; index++)
            result.AppendLine(
                Row(McpFormat.Invariant(offset + index + 1, "0"), page[index], nested: false)
            );
        result.AppendLine();
        AppendRowNotes(result, read);
        result.Append(McpOutput.PagedTruncationNote(page.Count, rows.Count, offset));
        return result.ToString().TrimEnd();
    }

    private static string RenderUltimate(
        StockOwnershipRead read,
        string label,
        int maxResults,
        int offset
    )
    {
        var groups = read.UltimateGroups;
        var page = groups.Skip(offset).Take(maxResults).ToList();
        if (page.Count == 0)
            return offset > 0
                ? McpOutput.PagedTruncationNote(0, groups.Count, offset)
                : $"No summed holders of {label}.";
        var result = MarkdownTable.Start(
            $"## Holders of {label} by ultimate parent",
            Subtitle(read, $"{McpFormat.WholeNumber(groups.Count)} ultimate parents"),
            "| # | Ultimate parent | CIK | Shares | % Out | 13F shares | Holders | Largest members |",
            "|---|-----------------|-----|--------|-------|------------|---------|-----------------|"
        );
        for (var index = 0; index < page.Count; index++)
        {
            var group = page[index];
            var members = string.Join(
                "; ",
                group
                    .Members.Take(3)
                    .Select(member => $"{member.Name} ({SourceText(member.Source)})")
            );
            result.AppendLine(
                $"| {offset + index + 1} | {MarkdownTable.EscapeCell(group.Name, "—")} | {group.UltimateCik ?? "—"} | "
                    + $"{McpFormat.WholeNumber(group.Shares)} | {Percent(group.PercentOutstanding)} | "
                    + $"{McpFormat.WholeNumber(group.Form13FShares)} | {McpFormat.WholeNumber(group.Members.Count)} | "
                    + $"{MarkdownTable.EscapeCell(members, "—")} |"
            );
        }
        result.AppendLine();
        result.AppendLine(
            "_Each group sums its 13F filers, which the 13F rules keep disjoint, plus the insider and 13D/G blocks no 13F report of the listing contains; funds are never summed. Parents come from SEC reporting links (other-manager lists, 13F notices) and recorded corporate parents, never from names._"
        );
        result.Append(McpOutput.PagedTruncationNote(page.Count, groups.Count, offset));
        return result.ToString().TrimEnd();
    }

    private static string RenderSummary(StockOwnershipRead read, string label, bool ultimate)
    {
        var summary = read.Summary;
        var result = MarkdownTable.Start(
            $"## Ownership summary of {label}",
            Subtitle(read, null),
            "| Holder group | Holders | Shares | % Out | Summed |",
            "|--------------|---------|--------|-------|--------|"
        );
        void Line(OwnershipSummaryLine line, string summed) =>
            result.AppendLine(
                $"| {line.Label} | {(line.Holders == 0 && line.Label == "Remaining" ? "—" : McpFormat.WholeNumber(line.Holders))} | "
                    + $"{McpFormat.WholeNumber(line.Shares)} | {Percent(line.PercentOutstanding)} | {summed} |"
            );
        Line(summary.Institutional, "yes");
        Line(summary.Insiders, summary.InsidersNotSummedReason == null ? "yes" : "no");
        Line(summary.Schedule13DGOnly, "yes");
        if (summary.Remaining != null)
            Line(summary.Remaining, summary.RemainingNegative ? "negative, see note" : "rest");
        Line(summary.Funds, "no");
        result.AppendLine();
        if (summary.SharesOutstanding is { } outstanding)
            result.AppendLine(
                $"_Shares outstanding: {McpFormat.WholeNumber(outstanding)}, the issuer's latest cover-page count restated onto today's split basis._"
            );
        if (summary.PercentUnavailableReason != null)
            result.AppendLine($"_% Out is unavailable: {summary.PercentUnavailableReason}._");
        if (summary.InsidersNotSummedReason != null)
            result.AppendLine(
                $"_Insiders are listed but not summed: {summary.InsidersNotSummedReason}._"
            );
        if (summary.RemainingNegative)
            result.AppendLine(
                "_Remaining is negative: the summed disclosures exceed shares outstanding (filings dated at different times, or shares counted twice by holders that do not disclose their links). It is flagged, not clipped._"
            );
        if (summary.SplitUnresolvedRows > 0)
            result.AppendLine(
                $"_{McpFormat.WholeNumber(summary.SplitUnresolvedRows)} summed rows keep their filed counts because a split since cannot be attributed to the listing._"
            );
        result.AppendLine(
            "_13D/G-only counts each filer's latest 13D or 13G that no 13F report of the listing contains and no counted insider files; filings naming one another's filers count once. Funds are never summed: their shares sit inside their advisers' 13F reports._"
        );

        AppendBreakdown(result, "By holder type (13F shares)", summary.ByHolderType);
        AppendBreakdown(
            result,
            summary.StyleAsOf is { } styleAsOf
                ? $"By style (13F shares, measured {FormatDate(styleAsOf)})"
                : "By style (13F shares, no measured quarter yet)",
            summary.ByStyle
        );
        AppendBreakdown(result, "By location (13F shares)", summary.ByLocation);

        if (ultimate)
        {
            result.AppendLine();
            result.AppendLine("### Largest ultimate parents");
            result.AppendLine();
            result.AppendLine("| # | Ultimate parent | CIK | Shares | % Out | Holders |");
            result.AppendLine("|---|-----------------|-----|--------|-------|---------|");
            var rank = 0;
            foreach (var group in read.UltimateGroups.Take(15))
                result.AppendLine(
                    $"| {++rank} | {MarkdownTable.EscapeCell(group.Name, "—")} | {group.UltimateCik ?? "—"} | "
                        + $"{McpFormat.WholeNumber(group.Shares)} | {Percent(group.PercentOutstanding)} | {McpFormat.WholeNumber(group.Members.Count)} |"
                );
        }
        return result.ToString().TrimEnd();
    }

    private static void AppendBreakdown(
        StringBuilder result,
        string title,
        IReadOnlyList<OwnershipBreakdownLine> lines
    )
    {
        if (lines.Count == 0)
            return;
        result.AppendLine();
        result.AppendLine($"### {title}");
        result.AppendLine();
        result.AppendLine("| Bucket | Holders | Shares | % Out |");
        result.AppendLine("|--------|---------|--------|-------|");
        foreach (var line in lines)
            result.AppendLine(
                $"| {line.Label} | {McpFormat.WholeNumber(line.Holders)} | {McpFormat.WholeNumber(line.Shares)} | {Percent(line.PercentOutstanding)} |"
            );
    }

    private static string RenderBeneficialOwnership(
        string label,
        int total,
        List<BeneficialOwnershipBlock> page,
        int offset
    )
    {
        var result = new StringBuilder();
        result.AppendLine($"## Schedule 13D and 13G filers of {label}");
        result.AppendLine(
            $"{McpFormat.WholeNumber(total)} filers, each on its latest 13D or 13G; shares restated onto today's split basis"
        );
        var rank = offset;
        foreach (var block in page)
        {
            rank++;
            var form = FormLabel(block.FilingType, block.IsAmendment);
            var change = block.ChangeShares is { } delta
                ? $", change {SignedWhole(delta)} since {FormatDate(block.PriorEventDate!.Value)}"
                : "";
            result.AppendLine();
            result.AppendLine(
                $"### {rank}. {MarkdownTable.EscapeCell(block.FilerName, "—")} (CIK {block.FilerCik ?? "—"})"
            );
            result.AppendLine(
                $"{form}, event {FormatDate(block.EventDate)}, filed {FormatDate(block.FilingDate)}, accession {block.AccessionNumber ?? "—"}"
                    + $"{RuleText(block.Rules)}: {McpFormat.WholeNumber(block.Shares)} shares"
                    + $"{(block.PercentOfClass is { } percent ? $", {McpFormat.Invariant(percent, "0.##")}% of class" : "")}{change}"
                    + $"{(block.SplitUnresolved ? " (as filed: a split since cannot be attributed)" : "")}"
            );
            if (block.Persons.Count == 0)
            {
                result.AppendLine();
                result.AppendLine(
                    block.HasCoverPage
                        ? "_No reporting persons were read from this filing._"
                        : "_This filing's cover pages were not read, so its reporting persons are unknown._"
                );
                continue;
            }
            result.AppendLine();
            result.AppendLine(
                "| Reporting person | CIK | Type | Shares | % of class | Sole vote | Shared vote | Sole disposition | Shared disposition |"
            );
            result.AppendLine(
                "|------------------|-----|------|--------|------------|-----------|-------------|------------------|--------------------|"
            );
            foreach (var person in block.Persons)
                result.AppendLine(
                    $"| {MarkdownTable.EscapeCell(person.Name, "—")}{(person.IsLead ? " (lead)" : "")} | {person.Cik ?? "—"} | {TypeCodes(person.Types)} | "
                        + $"{McpFormat.WholeNumber(person.AggregateAmountOwned)} | {(person.PercentOfClass is { } p ? McpFormat.Invariant(p, "0.##") + "%" : "—")} | "
                        + $"{McpFormat.WholeNumber(person.SoleVotingPower)} | {McpFormat.WholeNumber(person.SharedVotingPower)} | "
                        + $"{McpFormat.WholeNumber(person.SoleDispositivePower)} | {McpFormat.WholeNumber(person.SharedDispositivePower)} |"
                );
        }
        result.AppendLine();
        result.AppendLine(
            "_Reporting persons are not additive: each states its own beneficial ownership of the same block, so the filing's block is the lead person's amount. A 13D or 13G stays in force until the filer amends it, so an old event date can still be current._"
        );
        result.Append(McpOutput.PagedTruncationNote(page.Count, total, offset));
        return result.ToString().TrimEnd();
    }

    private static StringBuilder Start(StockOwnershipRead read, string label, string count) =>
        MarkdownTable.Start(
            $"## Holders of {label}",
            Subtitle(read, count),
            "| # | Holder | ID | Source | As of | Shares | Change | % Out | Value | Type | Notes |",
            "|---|--------|----|--------|-------|--------|--------|-------|-------|------|-------|"
        );

    private static string Subtitle(StockOwnershipRead read, string count)
    {
        var parts = new List<string>();
        if (count != null)
            parts.Add(count);
        if (read.Anchor is { } anchor)
            parts.Add(
                anchor.IsCombined
                    ? $"13F quarter {FormatDate(anchor.ReportDate)}, combined view (the filing window is open, so filers that have not filed yet carry their {FormatDate(anchor.PreviousReportDate!.Value)} positions)"
                    : $"13F quarter {FormatDate(anchor.ReportDate)}"
            );
        if (read.ExactListing)
            parts.Add("exact listing: insiders and % Out are issuer-wide, so they are not shown");
        return string.Join("; ", parts);
    }

    private static string Row(string rank, StockHolderRow row, bool nested)
    {
        var notes = new List<string>();
        if (!string.IsNullOrEmpty(row.Detail))
            notes.Add(row.Detail);
        if (row.Carried)
            notes.Add("carried, not yet filed for the newest quarter");
        if (row.Has(StockHolderRowFlags.Stale))
            notes.Add("stale");
        if (row.Has(StockHolderRowFlags.SplitUnresolved))
            notes.Add("as filed, split since unattributed");
        if (row.Has(StockHolderRowFlags.IssuerWide))
            notes.Add("issuer-wide");
        if (row.Has(StockHolderRowFlags.NotSummed))
            notes.Add("not summed");
        var change = row.ChangeShares is { } delta ? SignedWhole(delta) : "—";
        return $"| {rank} | {(nested ? "↳ " : "")}{MarkdownTable.EscapeCell(row.Name, "—")} | {IdText(row.HolderKey)} | {SourceText(row.Source)} | "
            + $"{FormatDate(row.AsOf)} | {McpFormat.WholeNumber(row.Shares)} | {change} | {Percent(row.PercentOutstanding)} | "
            + $"{(row.Value is { } value ? McpFormat.CompactUsd(value) : "—")} | {HolderBucketTokens.Label(row.EntityType)} | "
            + $"{MarkdownTable.EscapeCell(string.Join("; ", notes), "—")} |";
    }

    private static void AppendRowNotes(StringBuilder result, StockOwnershipRead read)
    {
        result.AppendLine(
            "_Shares are restated onto today's split basis. Change compares each row with the same holder's previous filing in the same source (13F quarter, 13D or 13G, Form 4, N-PORT report); a dash means no comparable previous filing. Rows nest by CIK and SEC reporting links, never by name; funds and nested 13D/G rows are not summed, since a 13F report already holds them._"
        );
        if (
            read.Summary.SharesOutstanding is { } outstanding
            && read.Summary.PercentUnavailableReason == null
        )
            result.AppendLine(
                $"_% Out is of {McpFormat.WholeNumber(outstanding)} shares outstanding._"
            );
        else if (read.Summary.PercentUnavailableReason != null)
            result.AppendLine($"_% Out is unavailable: {read.Summary.PercentUnavailableReason}._");
    }

    private static bool MatchesSource(StockHolderSource rowSource, string source) =>
        source switch
        {
            "all" => true,
            "13f" => rowSource == StockHolderSource.Form13F,
            "13dg" => rowSource is StockHolderSource.Schedule13D or StockHolderSource.Schedule13G,
            "13d" => rowSource == StockHolderSource.Schedule13D,
            "13g" => rowSource == StockHolderSource.Schedule13G,
            "insider" => rowSource == StockHolderSource.Insider,
            "fund" => rowSource == StockHolderSource.Fund,
            _ => false,
        };

    private static string SourceLabel(string source) =>
        source switch
        {
            "13f" => "13F",
            "13dg" => "13D and 13G",
            "13d" => "13D",
            "13g" => "13G",
            "insider" => "insider",
            "fund" => "fund",
            _ => "holder",
        };

    private static string SourceText(StockHolderSource source) =>
        source switch
        {
            StockHolderSource.Form13F => "13F",
            StockHolderSource.Schedule13D => "13D",
            StockHolderSource.Schedule13G => "13G",
            StockHolderSource.Insider => "Insider",
            StockHolderSource.Fund => "Fund",
            StockHolderSource.NonUs => "Non-US",
            _ => source.ToString(),
        };

    private static string IdText(StockHolderKey key) =>
        key.Kind switch
        {
            StockHolderKeyKind.Cik => $"CIK {key.Value}",
            StockHolderKeyKind.SeriesId => key.Value,
            StockHolderKeyKind.Lei => $"LEI {key.Value}",
            _ => "—",
        };

    private static string FormLabel(FilingType type, bool amendment) =>
        (type == FilingType.Schedule13D ? "SC 13D" : "SC 13G") + (amendment ? "/A" : "");

    private static string RuleText(Schedule13GRules rules)
    {
        var parts = new List<string>();
        if (rules.HasFlag(Schedule13GRules.QualifiedInstitution))
            parts.Add("13d-1(b)");
        if (rules.HasFlag(Schedule13GRules.PassiveInvestor))
            parts.Add("13d-1(c)");
        if (rules.HasFlag(Schedule13GRules.ExemptInvestor))
            parts.Add("13d-1(d)");
        return parts.Count == 0 ? "" : $", Rule {string.Join(", ", parts)}";
    }

    private static string TypeCodes(ReportingPersonTypes types)
    {
        if (types == ReportingPersonTypes.None)
            return "—";
        var codes = Enum.GetValues<ReportingPersonTypes>()
            .Where(flag => flag != ReportingPersonTypes.None && types.HasFlag(flag))
            .Select(flag =>
            {
                var name = flag.NameForHumans();
                var open = name.LastIndexOf('(');
                return open >= 0 ? name[(open + 1)..].TrimEnd(')') : name;
            });
        return string.Join(", ", codes);
    }

    private static string ListingLabel(EquityIssuer stock, string listedTicker) =>
        SecondaryTickerPolicy.RequiresExactListingScope(stock, listedTicker)
            ? listedTicker
            : $"{stock.Name} ({listedTicker})";

    private static string Percent(decimal? value) =>
        value is { } percent ? McpFormat.Invariant(percent, "F2") + "%" : "—";

    private static string SignedWhole(long value) =>
        (value > 0 ? "+" : "") + McpFormat.WholeNumber(value);

    private static string FormatDate(DateOnly date) => McpFormat.Invariant(date, "yyyy-MM-dd");
}
