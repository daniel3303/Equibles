using System.ComponentModel;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.CommonStocks.Repositories.Extensions;
using Equibles.Core.Extensions;
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
public class NCenTools
{
    private readonly NCenFilingRepository _nCenRepository;
    private readonly CommonStockRepository _commonStockRepository;
    private readonly FundSeriesRepository _fundSeriesRepository;
    private readonly McpToolRunner _runner;

    public NCenTools(
        NCenFilingRepository nCenRepository,
        CommonStockRepository commonStockRepository,
        FundSeriesRepository fundSeriesRepository,
        ErrorManager errorManager,
        ILogger<NCenTools> logger
    )
    {
        _nCenRepository = nCenRepository;
        _commonStockRepository = commonStockRepository;
        _fundSeriesRepository = fundSeriesRepository;
        _runner = new McpToolRunner(logger, errorManager.AsMcpErrorReporter());
    }

    [McpServerTool(
        Name = "GetFundNcenReports",
        Title = "Fund Operations (Form N-CEN)",
        ReadOnly = true
    )]
    [Description(
        "Get operational data for a registered investment company from its SEC Form N-CEN annual reports. Accepts an exchange-listed ticker or an exact fund identifier from SearchFunds, including a profile id, SEC series id, stored series ticker, or verified share-class alias. Each N-CEN shows the registrant's classification, Investment Company Act file number, reporting period, first/last-filing flags, latest service providers, and an exact filed-name provider history. N-CEN is filed at registrant level; this dataset currently ingests it through tracked issuer feeds, so a series inside an untracked multi-series trust can resolve correctly but still have no N-CEN report on record. Only registered funds file N-CEN; operating companies return no data."
    )]
    public Task<string> GetFundNcenReports(
        [Description(
            "Fund or ETF ticker, profile id, SEC series id, or verified share-class alias (e.g., MXF, IVV, S000004344, VOO)"
        )]
            string fund,
        [Description("Maximum number of annual reports to return (default: 10, max: 500)")]
            int maxResults = 10
    )
    {
        return _runner.Execute(
            async () =>
            {
                if (string.IsNullOrWhiteSpace(fund))
                    return "Provide a fund ticker or an exact identifier from SearchFunds.";

                var safeFund = MarkdownText(fund);
                var series = await _fundSeriesRepository
                    .ResolveIdentifier(fund)
                    .OrderByDescending(f => f.NetAssets)
                    .FirstOrDefaultAsync();
                CommonStock stock = null;
                if (series != null)
                {
                    if (series.CommonStockId == null)
                    {
                        return $"'{safeFund}' resolves to {MarkdownText(series.SeriesName ?? series.RegistrantName)}"
                            + (series.Ticker == null ? "" : $" ({MarkdownText(series.Ticker)})")
                            + $", a series of {MarkdownText(series.RegistrantName) ?? "its registered-fund trust"}. Form N-CEN is filed at registrant level, but this dataset currently ingests N-CEN through tracked issuer feeds and has no registrant-level report on record for this untracked multi-series trust. This is a coverage result, not an identifier-resolution failure.";
                    }

                    stock = await _commonStockRepository
                        .GetByIds([series.CommonStockId.Value])
                        .FirstOrDefaultAsync();
                    if (stock == null)
                        return $"'{safeFund}' resolves to {MarkdownText(series.SeriesName ?? series.RegistrantName)}, but its linked tracked issuer is no longer available; no Form N-CEN report can be selected. This is a coverage result, not evidence that the fund has no N-CEN filing.";
                }
                else
                {
                    (stock, _) = await _commonStockRepository.ResolveByTicker(fund);
                    if (stock == null)
                        return $"No registered fund found for '{safeFund}' in the tracked Form NPORT-P/N-CEN datasets. Use SearchFunds to find an exact profile id. Registered management investment companies and ETFs are in scope; vehicles outside those filing regimes may be absent, and fixed-income-only series can be missing from the tracked NPORT-P directory. This is a coverage result, not evidence that the fund does not exist.";
                }

                var filings = await _nCenRepository
                    .GetByStock(stock)
                    .Include(f => f.ServiceProviders)
                    .OrderByDescending(f => f.FilingDate)
                    .ThenByDescending(f => f.IsAmendment)
                    .ThenByDescending(f => f.AccessionNumber)
                    .ThenByDescending(f => f.CreationTime)
                    .Take(McpLimit.Clamp(maxResults))
                    .ToListAsync();

                if (filings.Count == 0)
                    return $"No Form N-CEN annual reports found for {MarkdownText(series?.SeriesName ?? stock.Name)} ({MarkdownText(stock.Ticker)}). Form N-CEN is registrant-level and this dataset currently ingests it through tracked issuer feeds. This is a coverage result, not evidence that the fund has no N-CEN filing.";

                var result = MarkdownTable.Start(
                    $"Form N-CEN annual reports for {MarkdownText(stock.Name)} ({safeFund}) — showing {filings.Count} most recent:",
                    "| Filed | Period End | Type | File Number | Amendment | First Filing | Last Filing |",
                    "|-------|------------|------|-------------|-----------|--------------|-------------|"
                );

                result.AppendRows(
                    filings,
                    f =>
                        $"| {f.FilingDate:yyyy-MM-dd} | {f.ReportEndingPeriod:yyyy-MM-dd} | {MarkdownText(FundCodes.RegistrationType(f.InvestmentCompanyType))} | {MarkdownText(f.InvestmentCompanyFileNumber) ?? "-"} | {(f.IsAmendment ? "Yes" : "No")} | {(f.IsFirstFiling ? "Yes" : "No")} | {(f.IsLastFiling ? "Yes" : "No")} |"
                );

                AppendServiceProviders(result, filings[0]);
                AppendServiceProviderHistory(result, filings);

                return result.ToString();
            },
            "GetFundNcenReports",
            $"fund: {fund}"
        );
    }

    private static void AppendServiceProviders(System.Text.StringBuilder result, NCenFiling latest)
    {
        // Providers are always sourced from the newest report only; say so explicitly when it
        // names none, or a consumer reads the missing section as "no providers on record" even
        // though an older report in the same response may list them.
        if (latest.ServiceProviders.Count == 0)
        {
            result.AppendLine();
            result.AppendLine(
                $"The latest report (filed {latest.FilingDate:yyyy-MM-dd}) names no service providers."
            );
            return;
        }

        result.AppendLine();
        result.AppendLine(
            $"Service providers reported on the latest report (filed {latest.FilingDate:yyyy-MM-dd}):"
        );
        result.AppendLine();
        result.AppendLine("| Role | Firm | Country | Affiliated |");
        result.AppendLine("|------|------|---------|------------|");

        result.AppendRows(
            latest.ServiceProviders.OrderBy(p => p.ProviderType).ThenBy(p => p.Name),
            provider =>
                $"| {provider.ProviderType.NameForHumans()} | {MarkdownText(provider.Name)} | {MarkdownText(provider.Country) ?? "-"} | {(provider.IsAffiliated ? "Yes" : "No")} |"
        );
    }

    // Historical provider rosters are the evidence needed to spot a changed auditor or custodian.
    // Render the exact filed-name timeline and compress only consecutive IDENTICAL snapshots. An
    // omitted role is an explicit "not reported" state, never a removal; punctuation differences
    // stay visible rather than being heuristically collapsed into one provider identity.
    private static void AppendServiceProviderHistory(
        System.Text.StringBuilder result,
        List<NCenFiling> filings
    )
    {
        if (filings.Count < 2)
            return;

        var oldestFirst = filings
            .OrderBy(f => f.FilingDate)
            .ThenBy(f => f.IsAmendment)
            .ThenBy(f => f.AccessionNumber)
            .ThenBy(f => f.CreationTime)
            .ToList();
        var sameDateFilings = oldestFirst
            .GroupBy(f => f.FilingDate)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToHashSet();
        var roles = oldestFirst
            .SelectMany(f => f.ServiceProviders.Select(p => p.ProviderType))
            .Distinct()
            .OrderBy(r => r.NameForHumans(), StringComparer.Ordinal);

        result.AppendLine();
        result.AppendLine(
            $"Service-provider history across the {filings.Count} reports shown (exact filed names; consecutive identical snapshots compressed):"
        );
        result.AppendLine();

        foreach (var role in roles)
        {
            string[] previous = null;
            var timeline = new List<string>();
            foreach (var filing in oldestFirst)
            {
                var current = NamesForRole(filing, role);
                if (previous != null && previous.SequenceEqual(current, StringComparer.Ordinal))
                    continue;
                timeline.Add(
                    $"{TimelineLabel(filing, sameDateFilings.Contains(filing.FilingDate))}: "
                        + (
                            current.Length == 0
                                ? "not reported"
                                : string.Join(", ", current.Select(MarkdownText))
                        )
                );
                previous = current;
            }
            result.AppendLine($"- {role.NameForHumans()}: {string.Join("; ", timeline)}");
        }
    }

    private static string[] NamesForRole(NCenFiling filing, NCenServiceProviderType role) =>
        filing
            .ServiceProviders.Where(p => p.ProviderType == role)
            .Select(p => p.Name)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

    private static string TimelineLabel(NCenFiling filing, bool disambiguate)
    {
        var date = filing.FilingDate.ToString("yyyy-MM-dd");
        if (!disambiguate)
            return date;

        var kind = filing.IsAmendment ? "amendment" : "original";
        var accession = MarkdownText(filing.AccessionNumber) ?? "accession unknown";
        return $"{date} ({kind}; accession {accession})";
    }

    // SEC-filed text can carry pipes and line breaks. Flatten it before inserting it into a
    // Markdown table or bullet so one provider cannot create a synthetic row or section.
    private static string MarkdownText(string value) =>
        value == null ? null : MarkdownTable.EscapeCell(value).Trim();
}
