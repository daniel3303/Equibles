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
        "Search SEC-registered investment advisers (Form ADV) by firm name. Returns matching advisory firms with their CRD number, main office location, regulatory assets under management and employee count, largest by assets first. Use the CRD number with GetInvestmentAdviser for full detail."
    )]
    public Task<string> SearchInvestmentAdvisers(
        [Description(
            "Part of the firm's legal or business name (e.g., \"Vanguard\", \"Renaissance\")"
        )]
            string query,
        [Description("Maximum number of advisers to return (default: 20)")] int maxResults = 20
    )
    {
        return _runner.Execute(
            async () =>
            {
                if (string.IsNullOrWhiteSpace(query))
                    return "Provide part of an adviser's name to search for.";

                maxResults = McpLimit.Clamp(maxResults);

                var advisers = await _adviserRepository
                    .Search(query)
                    .Take(maxResults)
                    .ToListAsync();

                if (advisers.Count == 0)
                    return $"No investment advisers found matching \"{query}\".";

                var result = MarkdownTable.Start(
                    $"Investment advisers matching \"{query}\" ({advisers.Count} shown, largest by assets first):",
                    "| CRD | Name | Location | Regulatory AUM | Employees |",
                    "|-----|------|----------|----------------|-----------|"
                );

                foreach (var a in advisers)
                {
                    result.AppendLine(
                        $"| {a.Crd} | {a.LegalName ?? "-"} | {FormatLocation(a)} | {FormatAum(a.TotalRegulatoryAum)} | {FormatCount(a.NumberOfEmployees)} |"
                    );
                }

                return result.ToString();
            },
            "SearchInvestmentAdvisers",
            $"query: {query}"
        );
    }

    [McpServerTool(Name = "GetInvestmentAdviser")]
    [Description(
        "Get the full Form ADV profile for a single SEC-registered investment adviser by its Organization CRD number: legal and business names, SEC file number, main office, website, regulatory assets under management (discretionary, non-discretionary and total), employee count, and how the firm is compensated (fee structure)."
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
                    return $"No investment adviser found with CRD {crd}.";

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
                    sb.AppendLine($"- **Website:** {adviser.WebsiteAddress}");
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
                sb.AppendLine($"- **As of:** {adviser.ReportDate:yyyy-MM-dd}");

                return sb.ToString();
            },
            "GetInvestmentAdviser",
            $"crd: {crd}"
        );
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
