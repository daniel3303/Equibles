using System.IO.Compression;
using System.Text;
using Equibles.Core.AutoWiring;
using Equibles.Holdings.HostedService.Models;

namespace Equibles.Holdings.HostedService.Services;

/// <summary>
/// Projects real-time-parsed 13F filings back into the exact TSV layout of
/// SEC's quarterly structured data set (SUBMISSION / COVERPAGE / INFOTABLE /
/// OTHERMANAGER2). Feeding this synthetic archive through the existing
/// <see cref="HoldingsImportService"/> guarantees the real-time path
/// reconciles byte-for-byte with the bulk path: identical dedup, amendment
/// delete-by-period, CUSIP/price resolution and upsert key. No persistence
/// logic is duplicated.
/// </summary>
[Service]
public class Realtime13FArchiveBuilder
{
    public ZipArchive Build(IReadOnlyCollection<Parsed13FFiling> filings)
    {
        var submission = new StringBuilder(
            "SUBMISSIONTYPE\tACCESSION_NUMBER\tFILING_DATE\tPERIODOFREPORT\tCIK\n"
        );
        var coverPage = new StringBuilder(
            "ACCESSION_NUMBER\tISAMENDMENT\tFILINGMANAGER_NAME\tFILINGMANAGER_CITY\t"
                + "FILINGMANAGER_STATEORCOUNTRY\tFORM13FFILENUMBER\tCRDNUMBER\n"
        );
        var infoTable = new StringBuilder(
            "ACCESSION_NUMBER\tCUSIP\tSSHPRNAMTTYPE\tPUTCALL\tSSHPRNAMT\tVOTING_AUTH_SOLE\t"
                + "VOTING_AUTH_SHARED\tVOTING_AUTH_NONE\tTITLEOFCLASS\tOTHERMANAGER\tINVESTMENTDISCRETION\n"
        );
        var otherManager = new StringBuilder("ACCESSION_NUMBER\tSEQUENCENUMBER\tNAME\n");

        foreach (var filing in filings)
        {
            var formType = filing.IsAmendment ? "13F-HR/A" : "13F-HR";

            AppendRow(
                submission,
                formType,
                Clean(filing.AccessionNumber),
                filing.FilingDate.ToString("yyyy-MM-dd"),
                filing.PeriodOfReport.ToString("yyyy-MM-dd"),
                Clean(filing.Cik)
            );

            AppendRow(
                coverPage,
                Clean(filing.AccessionNumber),
                filing.IsAmendment ? "Y" : "N",
                Clean(filing.FilingManagerName),
                Clean(filing.City),
                Clean(filing.StateOrCountry),
                Clean(filing.Form13FFileNumber),
                Clean(filing.CrdNumber)
            );

            foreach (var (seq, name) in filing.OtherManagers)
            {
                AppendRow(otherManager, Clean(filing.AccessionNumber), seq, Clean(name));
            }

            foreach (var holding in filing.Holdings)
            {
                AppendRow(
                    infoTable,
                    Clean(filing.AccessionNumber),
                    Clean(holding.Cusip),
                    Clean(holding.ShareType),
                    Clean(holding.PutCall),
                    holding.Shares,
                    holding.VotingAuthSole,
                    holding.VotingAuthShared,
                    holding.VotingAuthNone,
                    Clean(holding.TitleOfClass),
                    holding.OtherManagerNumber?.ToString() ?? string.Empty,
                    Clean(holding.InvestmentDiscretion)
                );
            }
        }

        // Build into a throwaway buffer, then hand the read archive an
        // independently-owned stream. Ownership is then unambiguous: disposing
        // the returned archive disposes its stream, and a failure while writing
        // disposes the build buffer here instead of leaking it.
        byte[] zipBytes;
        using (var buffer = new MemoryStream())
        {
            using (var writer = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
            {
                WriteEntry(writer, "SUBMISSION.tsv", submission);
                WriteEntry(writer, "COVERPAGE.tsv", coverPage);
                WriteEntry(writer, "INFOTABLE.tsv", infoTable);
                WriteEntry(writer, "OTHERMANAGER2.tsv", otherManager);
            }
            zipBytes = buffer.ToArray();
        }

        // Disposing the returned archive transitively disposes this stream.
        return new ZipArchive(new MemoryStream(zipBytes), ZipArchiveMode.Read);
    }

    // Bulk-dataset TSV rows are tab-separated and newline-terminated; AppendJoin
    // handles each field via value.ToString(), matching the per-overload Append
    // calls byte-for-byte so behavior reconciles with the bulk import path.
    private static void AppendRow(StringBuilder sb, params object[] fields)
    {
        sb.AppendJoin('\t', fields).Append('\n');
    }

    private static void WriteEntry(ZipArchive archive, string name, StringBuilder content)
    {
        var entry = archive.CreateEntry(name);
        using var stream = entry.Open();
        var bytes = Encoding.UTF8.GetBytes(content.ToString());
        stream.Write(bytes, 0, bytes.Length);
    }

    /// <summary>
    /// The TSV reader splits on tabs and newlines; any of those embedded in a
    /// free-text field (manager names, titles) would corrupt the whole row.
    /// Replace them with spaces — the import pipeline trims values anyway.
    /// </summary>
    private static string Clean(string value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;
        return value.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
    }
}
