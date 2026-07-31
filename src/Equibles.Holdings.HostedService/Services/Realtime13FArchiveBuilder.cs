using System.Globalization;
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
            "ACCESSION_NUMBER\tISAMENDMENT\tAMENDMENTTYPE\tFILINGMANAGER_NAME\tFILINGMANAGER_CITY\t"
                + "FILINGMANAGER_STATEORCOUNTRY\tFORM13FFILENUMBER\tCRDNUMBER\t"
                + "CONFIDENTIALTREATMENT\n"
        );
        var infoTable = new StringBuilder(
            "ACCESSION_NUMBER\tCUSIP\tSSHPRNAMTTYPE\tPUTCALL\tVALUE\tSSHPRNAMT\tVOTING_AUTH_SOLE\t"
                + "VOTING_AUTH_SHARED\tVOTING_AUTH_NONE\tTITLEOFCLASS\tOTHERMANAGER\tINVESTMENTDISCRETION\n"
        );
        // Both other-manager sections carry the SEC's identifier columns, in its column order, so
        // the synthetic archive stays parseable by the same reader as the quarterly one.
        var otherManager = new StringBuilder(
            "ACCESSION_NUMBER\tSEQUENCENUMBER\tCIK\tFORM13FFILENUMBER\tCRDNUMBER\tSECFILENUMBER\t"
                + "NAME\n"
        );
        var otherManagerCover = new StringBuilder(
            "ACCESSION_NUMBER\tOTHERMANAGER_SK\tCIK\tFORM13FFILENUMBER\tCRDNUMBER\tSECFILENUMBER\t"
                + "NAME\n"
        );
        var summaryPage = new StringBuilder("ACCESSION_NUMBER\tTABLEENTRYTOTAL\tTABLEVALUETOTAL\n");

        foreach (var filing in filings)
        {
            var formType = filing.IsAmendment ? "13F-HR/A" : "13F-HR";

            AppendRow(
                submission,
                formType,
                Clean(filing.AccessionNumber),
                filing.FilingDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                filing.PeriodOfReport.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                Clean(filing.Cik)
            );

            AppendRow(
                coverPage,
                Clean(filing.AccessionNumber),
                filing.IsAmendment ? "Y" : "N",
                Clean(filing.AmendmentType),
                Clean(filing.FilingManagerName),
                Clean(filing.City),
                Clean(filing.StateOrCountry),
                Clean(filing.Form13FFileNumber),
                Clean(filing.CrdNumber),
                filing.ConfidentialTreatmentRequested ? "Y" : "N"
            );

            foreach (var (seq, identity) in filing.OtherManagers)
            {
                AppendRow(
                    otherManager,
                    Clean(filing.AccessionNumber),
                    seq,
                    Clean(identity.Cik),
                    Clean(identity.Form13FFileNumber),
                    Clean(identity.CrdNumber),
                    Clean(identity.SecFileNumber),
                    Clean(identity.Name)
                );
            }

            // The cover-page list files no sequence numbers, so the surrogate key is positional —
            // it preserves filed order through the archive and nothing points at it.
            for (var index = 0; index < filing.CoverPageOtherManagers.Count; index++)
            {
                var identity = filing.CoverPageOtherManagers[index];
                AppendRow(
                    otherManagerCover,
                    Clean(filing.AccessionNumber),
                    index + 1,
                    Clean(identity.Cik),
                    Clean(identity.Form13FFileNumber),
                    Clean(identity.CrdNumber),
                    Clean(identity.SecFileNumber),
                    Clean(identity.Name)
                );
            }

            // Emitted even when both totals are null (empty cells): the import parses the row
            // tolerantly, and a consistently-present section keeps the synthetic archive the
            // same shape as the SEC's quarterly one.
            AppendRow(
                summaryPage,
                Clean(filing.AccessionNumber),
                filing.TableEntryTotal?.ToString() ?? string.Empty,
                filing.TableValueTotal?.ToString() ?? string.Empty
            );

            foreach (var holding in filing.Holdings)
            {
                AppendRow(
                    infoTable,
                    Clean(filing.AccessionNumber),
                    Clean(holding.Cusip),
                    Clean(holding.ShareType),
                    Clean(holding.PutCall),
                    holding.Value,
                    holding.Shares,
                    holding.VotingAuthSole,
                    holding.VotingAuthShared,
                    holding.VotingAuthNone,
                    Clean(holding.TitleOfClass),
                    Clean(holding.OtherManagers),
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
                WriteEntry(writer, "OTHERMANAGER.tsv", otherManagerCover);
                WriteEntry(writer, "SUMMARYPAGE.tsv", summaryPage);
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
