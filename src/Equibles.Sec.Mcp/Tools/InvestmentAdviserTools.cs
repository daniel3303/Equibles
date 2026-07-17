using System.ComponentModel;
using System.Text;
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
public class InvestmentAdviserTools
{
    private readonly FormAdvAdviserRepository _adviserRepository;
    private readonly McpToolRunner _runner;

    public InvestmentAdviserTools(
        FormAdvAdviserRepository adviserRepository,
        ErrorManager errorManager,
        ILogger<InvestmentAdviserTools> logger
    )
    {
        _adviserRepository = adviserRepository;
        _runner = new McpToolRunner(logger, errorManager.AsMcpErrorReporter());
    }

    [McpServerTool(Name = "SearchInvestmentAdvisers")]
    [Description(
        "Search SEC-registered investment advisers (Form ADV) by firm name. Returns matching advisory firms with their CRD number, main office location, regulatory assets under management, employee count and the as-of date of their latest Form ADV data, largest by assets first. Use the CRD number with GetInvestmentAdviser for full detail."
    )]
    public Task<string> SearchInvestmentAdvisers(
        [Description(
            "Part of the firm's legal or business name (e.g., \"Vanguard\", \"Renaissance\")"
        )]
            string query,
        [Description("Maximum number of advisers to return (default: 20, clamped to 1-500)")]
            int maxResults = 20
    )
    {
        return _runner.Execute(
            async () =>
            {
                if (string.IsNullOrWhiteSpace(query))
                    return "Provide part of an adviser's name to search for.";

                maxResults = McpLimit.Clamp(maxResults);

                var totalMatches = await _adviserRepository.Search(query).CountAsync();
                var advisers = await _adviserRepository
                    .Search(query)
                    .Take(maxResults)
                    .ToListAsync();

                var table = MarkdownTable.Render(
                    advisers,
                    $"No investment advisers found matching \"{query}\".",
                    $"Investment advisers matching \"{query}\" ({advisers.Count} of {totalMatches} matches shown, largest by assets first):",
                    "| CRD | Name | Location | Regulatory AUM | Employees | As of |",
                    "|-----|------|----------|----------------|-----------|-------|",
                    a =>
                        $"| {a.Crd} | {FormatName(a)} | {FormatLocation(a)} | {FormatAum(a.TotalRegulatoryAum)} | {FormatCount(a.NumberOfEmployees)} | {a.ReportDate:yyyy-MM-dd} |"
                );
                if (advisers.Count == 0)
                    return table;

                var truncation = McpOutput.TruncationNote(advisers.Count, totalMatches);
                var footer =
                    "_Figures come from each firm's latest Form ADV filing via the SEC's monthly bulk extract (the As-of column)._";
                return truncation.Length == 0
                    ? $"{table}\n{footer}"
                    : $"{table}\n{truncation}\n{footer}";
            },
            "SearchInvestmentAdvisers",
            $"query: {query}"
        );
    }

    // A firm matched via its doing-business-as name would otherwise render only a legal name
    // containing no trace of the query, which reads as a false positive.
    private static string FormatName(FormAdvAdviser a)
    {
        var legal = a.LegalName ?? "-";
        return
            !string.IsNullOrEmpty(a.PrimaryBusinessName)
            && !string.Equals(
                a.PrimaryBusinessName,
                a.LegalName,
                StringComparison.OrdinalIgnoreCase
            )
            ? $"{legal} (dba {a.PrimaryBusinessName})"
            : legal;
    }

    [McpServerTool(Name = "GetInvestmentAdviser")]
    [Description(
        "Get the full Form ADV profile for a single SEC-registered investment adviser by its Organization CRD number: legal and business names, SEC file number, main office, website, regulatory assets under management (discretionary, non-discretionary and total), employee count, and how the firm is compensated (fee structure). Find CRD numbers with SearchInvestmentAdvisers."
    )]
    public Task<string> GetInvestmentAdviser(
        [Description("The adviser's Organization CRD number (e.g., 231)")] int crd
    )
    {
        return _runner.Execute(
            async () =>
            {
                var adviser = await _adviserRepository.GetByCrd(crd).FirstOrDefaultAsync();

                if (adviser == null)
                    return $"No investment adviser found with CRD {crd}. Search by firm name with SearchInvestmentAdvisers to find the right CRD.";

                var sb = new StringBuilder();
                sb.AppendLine($"# {adviser.LegalName ?? $"Adviser CRD {adviser.Crd}"}");
                if (
                    !string.IsNullOrEmpty(adviser.PrimaryBusinessName)
                    && !string.Equals(
                        adviser.PrimaryBusinessName,
                        adviser.LegalName,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    sb.AppendLine($"Doing business as: {adviser.PrimaryBusinessName}");
                }
                sb.AppendLine();
                sb.AppendLine($"- **CRD number:** {adviser.Crd}");
                if (!string.IsNullOrEmpty(adviser.SecNumber))
                    sb.AppendLine($"- **SEC file number:** {adviser.SecNumber}");
                sb.AppendLine($"- **Main office:** {FormatLocation(adviser)}");
                if (!string.IsNullOrEmpty(adviser.WebsiteAddress))
                    sb.AppendLine(
                        $"- **Website (as filed on Form ADV):** {adviser.WebsiteAddress}{SocialMediaSuffix(adviser.WebsiteAddress)}"
                    );
                if (!string.IsNullOrEmpty(adviser.SecStatus))
                    sb.AppendLine($"- **SEC status:** {adviser.SecStatus}");
                sb.AppendLine($"- **Employees:** {FormatCount(adviser.NumberOfEmployees)}");
                sb.AppendLine(
                    $"- **Total regulatory AUM:** {FormatAum(adviser.TotalRegulatoryAum)}"
                );
                sb.AppendLine($"- **Discretionary AUM:** {FormatAum(adviser.DiscretionaryAum)}");
                sb.AppendLine(
                    $"- **Non-discretionary AUM:** {FormatAum(adviser.NonDiscretionaryAum)}"
                );
                sb.AppendLine($"- **Fee structure:** {FormatFeeStructure(adviser)}");
                sb.AppendLine(
                    $"- **SEC data snapshot:** {adviser.ReportDate:yyyy-MM-dd} (monthly Form ADV bulk extract; figures come from the adviser's most recent filing, which can be older)"
                );

                return sb.ToString();
            },
            "GetInvestmentAdviser",
            $"crd: {crd}"
        );
    }

    // Item 1.I of Form ADV accepts social-media accounts as "website addresses", and some
    // firms file only those (Vanguard lists an X handle), so an unannotated pass-through
    // reads as the corporate site.
    private static readonly string[] SocialMediaHosts =
    [
        "x.com",
        "twitter.com",
        "linkedin.com",
        "facebook.com",
        "instagram.com",
        "youtube.com",
    ];

    private static string SocialMediaSuffix(string websiteAddress)
    {
        if (!Uri.TryCreate(websiteAddress.Trim().ToLowerInvariant(), UriKind.Absolute, out var uri))
            return string.Empty;
        var host = uri.Host.StartsWith("www.") ? uri.Host[4..] : uri.Host;
        return SocialMediaHosts.Contains(host)
            ? " (a social-media account, as filed)"
            : string.Empty;
    }

    private static string FormatLocation(FormAdvAdviser a)
    {
        var parts = new[] { a.MainOfficeCity, a.MainOfficeState, a.MainOfficeCountry }
            .Where(p => !string.IsNullOrEmpty(p))
            .ToArray();
        return parts.Length == 0 ? "-" : string.Join(", ", parts);
    }

    private static string FormatAum(long? amount) =>
        amount.HasValue ? $"${McpFormat.WholeNumber(amount.Value)}" : "-";

    private static string FormatCount(int? count) =>
        count.HasValue ? McpFormat.WholeNumber(count.Value) : "-";

    private static string FormatFeeStructure(FormAdvAdviser a)
    {
        var fees = new List<string>();
        if (a.ChargesPercentageOfAum)
            fees.Add("percentage of AUM");
        if (a.ChargesHourly)
            fees.Add("hourly");
        if (a.ChargesSubscription)
            fees.Add("subscription");
        if (a.ChargesFixed)
            fees.Add("fixed fees");
        if (a.ChargesCommissions)
            fees.Add("commissions");
        if (a.ChargesPerformanceBased)
            fees.Add("performance-based");
        if (a.ChargesOther)
            fees.Add("other");
        return fees.Count == 0 ? "-" : string.Join(", ", fees);
    }
}
