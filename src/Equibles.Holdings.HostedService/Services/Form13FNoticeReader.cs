using System.IO.Compression;
using Equibles.Core.Extensions;
using Equibles.Holdings.Data.Models;
using static Equibles.Holdings.HostedService.Services.HoldingsParsingHelper;

namespace Equibles.Holdings.HostedService.Services;

/// <summary>
/// Reads the Form 13F notices (13F-NT and 13F-NT/A) out of a 13F data set: the submission, its
/// cover page and the cover page's list of managers who report for the filer. Holdings reports
/// in the same archive are left to the holdings importer.
/// </summary>
internal static class Form13FNoticeReader
{
    internal static async Task<List<Form13FNotice>> Read(
        ZipArchive archive,
        string dataSetFileName,
        TsvParser tsvParser
    )
    {
        var submissions =
            FindEntry(archive, "SUBMISSION.tsv")
            ?? throw new InvalidDataException("The 13F data set has no SUBMISSION.tsv table.");

        var notices = new Dictionary<string, Form13FNotice>(StringComparer.OrdinalIgnoreCase);
        await foreach (var row in tsvParser.ParseEntry(submissions))
        {
            var formType = GetValue(row, "SUBMISSIONTYPE")?.Trim().ToUpperInvariant();
            if (formType is not ("13F-NT" or "13F-NT/A"))
                continue;
            var accession = NormalizeIdentifier(GetValue(row, "ACCESSION_NUMBER"));
            var cik = NormalizeCik(GetValue(row, "CIK"));
            if (
                accession == null
                || cik == null
                || !TryParseDateOnly(GetValue(row, "PERIODOFREPORT"), out var reportDate)
                || !TryParseDateOnly(GetValue(row, "FILING_DATE"), out var filingDate)
            )
                continue;

            notices[accession] = new Form13FNotice
            {
                AccessionNumber = Clamp(accession, 32),
                FilerCik = Clamp(cik, 16),
                ReportDate = reportDate,
                FilingDate = filingDate,
                IsAmendment = formType == "13F-NT/A",
                DataSetFileName = Clamp(dataSetFileName, 128),
            };
        }

        if (notices.Count == 0)
            return [];

        var coverPages = FindEntry(archive, "COVERPAGE.tsv");
        if (coverPages != null)
        {
            await foreach (var row in tsvParser.ParseEntry(coverPages))
            {
                if (!notices.TryGetValue(GetValue(row, "ACCESSION_NUMBER") ?? "", out var notice))
                    continue;
                notice.FilerName = Clamp(
                    NormalizeIdentifier(GetValue(row, "FILINGMANAGER_NAME")),
                    512
                );
                notice.Form13FFileNumber = Identifier(row, "FORM13FFILENUMBER");
                notice.CrdNumber = Identifier(row, "CRDNUMBER");
                notice.SecFileNumber = Identifier(row, "SECFILENUMBER");
            }
        }

        var otherManagers = FindEntry(archive, "OTHERMANAGER.tsv");
        if (otherManagers != null)
        {
            var reporters =
                new List<(Form13FNotice Notice, long Sort, Form13FNoticeReporter Reporter)>();
            var position = 0L;
            await foreach (var row in tsvParser.ParseEntry(otherManagers))
            {
                position++;
                if (!notices.TryGetValue(GetValue(row, "ACCESSION_NUMBER") ?? "", out var notice))
                    continue;
                var name = NormalizeIdentifier(GetValue(row, "NAME"));
                if (name == null)
                    continue;

                // SEC's surrogate key orders the list as filed; file position is the fallback.
                var sort = long.TryParse(GetValue(row, "OTHERMANAGER_SK"), out var key)
                    ? key
                    : position;
                reporters.Add(
                    (
                        notice,
                        sort,
                        new Form13FNoticeReporter
                        {
                            Name = Clamp(name, 256),
                            Cik = Clamp(NormalizeCik(GetValue(row, "CIK")), 16),
                            Form13FFileNumber = Identifier(row, "FORM13FFILENUMBER"),
                            CrdNumber = Identifier(row, "CRDNUMBER"),
                            SecFileNumber = Identifier(row, "SECFILENUMBER"),
                        }
                    )
                );
            }

            foreach (var group in reporters.GroupBy(item => item.Notice))
            {
                var ordinal = 0;
                foreach (var item in group.OrderBy(item => item.Sort))
                {
                    item.Reporter.Ordinal = ++ordinal;
                    group.Key.Reporters.Add(item.Reporter);
                }
            }
        }

        return notices.Values.ToList();
    }

    private static string Identifier(Dictionary<string, string> row, string column) =>
        Clamp(NormalizeIdentifier(GetValue(row, column)), 32);

    private static string Clamp(string value, int maxLength) => value.TruncateToFit(maxLength);
}
