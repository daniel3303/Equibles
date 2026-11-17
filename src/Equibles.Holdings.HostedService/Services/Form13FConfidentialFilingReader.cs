using System.IO.Compression;
using Equibles.Core.Extensions;
using Equibles.Holdings.Data.Models;
using static Equibles.Holdings.HostedService.Services.HoldingsParsingHelper;

namespace Equibles.Holdings.HostedService.Services;

/// <summary>
/// Reads, out of a 13F data set, the holdings reports (13F-HR and 13F-HR/A) whose summary page
/// declares positions omitted under a confidential treatment request, with how many positions
/// each one still discloses.
/// </summary>
internal static class Form13FConfidentialFilingReader
{
    private const string OmittedColumn = "ISCONFIDENTIALOMITTED";

    /// <param name="ColumnSeen">
    /// False when the summary page has rows and none carries the omission column: the layout
    /// changed, and an empty answer is then not evidence that nothing was withheld.
    /// </param>
    internal sealed record Result(List<Form13FConfidentialFiling> Filings, bool ColumnSeen);

    internal static async Task<Result> Read(ZipArchive archive, TsvParser tsvParser)
    {
        var submissions = FindEntry(archive, "SUBMISSION.tsv");
        var summaryPages = FindEntry(archive, "SUMMARYPAGE.tsv");
        // A data set without a summary page declares nothing to read.
        if (submissions == null || summaryPages == null)
            return new Result([], true);

        var declaredTotals = new Dictionary<string, int?>(StringComparer.OrdinalIgnoreCase);
        var summaryRows = 0;
        var columnSeen = false;
        await foreach (var row in tsvParser.ParseEntry(summaryPages))
        {
            summaryRows++;
            columnSeen |= row.ContainsKey(OmittedColumn);
            var accession = NormalizeIdentifier(GetValue(row, "ACCESSION_NUMBER"));
            if (
                accession == null
                || !HoldingsImportService.IsYes(GetValue(row, OmittedColumn)?.Trim())
            )
                continue;
            declaredTotals[accession] =
                int.TryParse(GetValue(row, "TABLEENTRYTOTAL"), out var entries) && entries >= 0
                    ? entries
                    : null;
        }
        if (declaredTotals.Count == 0)
            return new Result([], columnSeen || summaryRows == 0);

        var filings = new Dictionary<string, Form13FConfidentialFiling>(
            StringComparer.OrdinalIgnoreCase
        );
        await foreach (var row in tsvParser.ParseEntry(submissions))
        {
            var formType = GetValue(row, "SUBMISSIONTYPE")?.Trim().ToUpperInvariant();
            if (formType is not ("13F-HR" or "13F-HR/A"))
                continue;
            var accession = NormalizeIdentifier(GetValue(row, "ACCESSION_NUMBER"));
            var cik = NormalizeCik(GetValue(row, "CIK"));
            if (
                accession == null
                || cik == null
                || !declaredTotals.TryGetValue(accession, out var declaredTotal)
                || !TryParseDateOnly(GetValue(row, "PERIODOFREPORT"), out var reportDate)
                || !TryParseDateOnly(GetValue(row, "FILING_DATE"), out var filingDate)
            )
                continue;

            filings[accession] = new Form13FConfidentialFiling
            {
                AccessionNumber = accession.TruncateToFit(32),
                FilerCik = cik.TruncateToFit(16),
                ReportDate = reportDate,
                FilingDate = filingDate,
                IsAmendment = formType == "13F-HR/A",
                DeclaredEntryTotal = declaredTotal,
            };
        }
        if (filings.Count == 0)
            return new Result([], true);

        // Without the information table a disclosed count of zero would read as a withheld book.
        var infoTable =
            FindEntry(archive, "INFOTABLE.tsv")
            ?? throw new InvalidDataException("The 13F data set has no INFOTABLE.tsv table.");
        await foreach (var row in tsvParser.ParseEntry(infoTable))
        {
            if (
                filings.TryGetValue(
                    NormalizeIdentifier(GetValue(row, "ACCESSION_NUMBER")) ?? "",
                    out var filing
                )
                && (
                    ParseLong(GetValue(row, "SSHPRNAMT")) > 0
                    || ParseLong(GetValue(row, "VALUE")) > 0
                )
            )
                filing.DisclosedPositionCount++;
        }

        return new Result(filings.Values.ToList(), true);
    }
}
