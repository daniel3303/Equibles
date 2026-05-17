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

            submission
                .Append(formType)
                .Append('\t')
                .Append(Clean(filing.AccessionNumber))
                .Append('\t')
                .Append(filing.FilingDate.ToString("yyyy-MM-dd"))
                .Append('\t')
                .Append(filing.PeriodOfReport.ToString("yyyy-MM-dd"))
                .Append('\t')
                .Append(Clean(filing.Cik))
                .Append('\n');

            coverPage
                .Append(Clean(filing.AccessionNumber))
                .Append('\t')
                .Append(filing.IsAmendment ? "Y" : "N")
                .Append('\t')
                .Append(Clean(filing.FilingManagerName))
                .Append('\t')
                .Append(Clean(filing.City))
                .Append('\t')
                .Append(Clean(filing.StateOrCountry))
                .Append('\t')
                .Append(Clean(filing.Form13FFileNumber))
                .Append('\t')
                .Append(Clean(filing.CrdNumber))
                .Append('\n');

            foreach (var (seq, name) in filing.OtherManagers)
            {
                otherManager
                    .Append(Clean(filing.AccessionNumber))
                    .Append('\t')
                    .Append(seq)
                    .Append('\t')
                    .Append(Clean(name))
                    .Append('\n');
            }

            foreach (var holding in filing.Holdings)
            {
                infoTable
                    .Append(Clean(filing.AccessionNumber))
                    .Append('\t')
                    .Append(Clean(holding.Cusip))
                    .Append('\t')
                    .Append(Clean(holding.ShareType))
                    .Append('\t')
                    .Append(Clean(holding.PutCall))
                    .Append('\t')
                    .Append(holding.Shares)
                    .Append('\t')
                    .Append(holding.VotingAuthSole)
                    .Append('\t')
                    .Append(holding.VotingAuthShared)
                    .Append('\t')
                    .Append(holding.VotingAuthNone)
                    .Append('\t')
                    .Append(Clean(holding.TitleOfClass))
                    .Append('\t')
                    .Append(holding.OtherManagerNumber?.ToString() ?? string.Empty)
                    .Append('\t')
                    .Append(Clean(holding.InvestmentDiscretion))
                    .Append('\n');
            }
        }

        var buffer = new MemoryStream();
        using (var writer = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(writer, "SUBMISSION.tsv", submission);
            WriteEntry(writer, "COVERPAGE.tsv", coverPage);
            WriteEntry(writer, "INFOTABLE.tsv", infoTable);
            WriteEntry(writer, "OTHERMANAGER2.tsv", otherManager);
        }

        buffer.Position = 0;
        return new ZipArchive(buffer, ZipArchiveMode.Read);
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
