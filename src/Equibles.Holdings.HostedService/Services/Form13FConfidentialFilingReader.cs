using System.IO.Compression;
using Equibles.Core.Extensions;
using Equibles.Holdings.Data.Models;
using static Equibles.Holdings.HostedService.Services.HoldingsParsingHelper;

namespace Equibles.Holdings.HostedService.Services;

/// <summary>
/// Reads, out of a 13F data set, the holdings reports (13F-HR and 13F-HR/A) whose summary page
/// declares positions omitted under a confidential treatment request.
/// </summary>
internal static class Form13FConfidentialFilingReader
{
    internal static async Task<List<Form13FConfidentialFiling>> Read(
        ZipArchive archive,
        TsvParser tsvParser
    )
    {
        var submissions = FindEntry(archive, "SUBMISSION.tsv");
        var summaryPages = FindEntry(archive, "SUMMARYPAGE.tsv");
        // The oldest data sets carry no summary page; they declare nothing to read.
        if (submissions == null || summaryPages == null)
            return [];

        var omitted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await foreach (var row in tsvParser.ParseEntry(summaryPages))
        {
            var accession = NormalizeIdentifier(GetValue(row, "ACCESSION_NUMBER"));
            if (accession != null && IsYes(GetValue(row, "ISCONFIDENTIALOMITTED")?.Trim()))
                omitted.Add(accession);
        }
        if (omitted.Count == 0)
            return [];

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
                || !omitted.Contains(accession)
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
            };
        }

        return filings.Values.ToList();
    }
}
