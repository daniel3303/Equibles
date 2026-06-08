using System.Globalization;
using System.IO.Compression;
using System.Text;
using Equibles.Core.AutoWiring;
using Equibles.Holdings.HostedService.Models;

namespace Equibles.Holdings.HostedService.Services;

/// <summary>
/// Projects parsed Schedule 13D/13G filings into the same TSV layout the
/// quarterly 13F data set uses, so beneficial-ownership filings flow through the
/// identical <see cref="HoldingsImportService"/> pipeline (CUSIP/price
/// resolution, holder upsert, amendment handling, upsert key) without
/// duplicating any persistence logic.
///
/// A 13D/13G lists every member of a reporting group — funds, their general
/// partner, the parent — each reporting overlapping stakes in the SAME issuer,
/// so the amounts are NOT additive. We attribute one position per filing to the
/// lead filer: the reporting person whose CIK matches the filer, else the
/// person reporting the largest aggregate (the top of the ownership chain, i.e.
/// the group's total beneficial ownership). The per-share value is left 0 — 13D/
/// 13G never report a dollar value, so the import pipeline derives it from the
/// share count and the period's closing price, exactly as it does for 13F.
/// </summary>
[Service]
public class Realtime13DGArchiveBuilder
{
    public ZipArchive Build(IReadOnlyCollection<Parsed13DGFiling> filings)
    {
        var submission = new StringBuilder(
            "SUBMISSIONTYPE\tACCESSION_NUMBER\tFILING_DATE\tPERIODOFREPORT\tCIK\n"
        );
        var coverPage = new StringBuilder(
            "ACCESSION_NUMBER\tISAMENDMENT\tAMENDMENTTYPE\tFILINGMANAGER_NAME\tFILINGMANAGER_CITY\t"
                + "FILINGMANAGER_STATEORCOUNTRY\tFORM13FFILENUMBER\tCRDNUMBER\t"
                + "CONFIDENTIALTREATMENT\n"
        );
        // PERCENTOFCLASS is appended after the 13F columns; the 13F INFOTABLE has
        // no such column, so HoldingsImportService reads it as null there.
        var infoTable = new StringBuilder(
            "ACCESSION_NUMBER\tCUSIP\tSSHPRNAMTTYPE\tPUTCALL\tVALUE\tSSHPRNAMT\tVOTING_AUTH_SOLE\t"
                + "VOTING_AUTH_SHARED\tVOTING_AUTH_NONE\tTITLEOFCLASS\tOTHERMANAGER\t"
                + "INVESTMENTDISCRETION\tPERCENTOFCLASS\n"
        );
        // 13D/13G carry no other-manager table; the file is still written (empty)
        // because the import pipeline expects the same archive shape as 13F.
        var otherManager = new StringBuilder("ACCESSION_NUMBER\tSEQUENCENUMBER\tNAME\n");

        foreach (var filing in filings)
        {
            var person = SelectLeadPerson(filing);
            if (person == null)
                continue;

            AppendRow(
                submission,
                Clean(filing.SubmissionType),
                Clean(filing.AccessionNumber),
                filing.FilingDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                filing.DateOfEvent.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                Clean(filing.FilerCik)
            );

            AppendRow(
                coverPage,
                Clean(filing.AccessionNumber),
                filing.IsAmendment ? "Y" : "N",
                string.Empty,
                Clean(person.Name),
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                "N"
            );

            AppendRow(
                infoTable,
                Clean(filing.AccessionNumber),
                Clean(filing.IssuerCusip),
                "SH",
                string.Empty,
                0,
                person.AggregateAmountOwned,
                person.SoleVotingPower,
                person.SharedVotingPower,
                0,
                Clean(filing.SecuritiesClassTitle),
                string.Empty,
                string.Empty,
                person.PercentOfClass?.ToString(CultureInfo.InvariantCulture) ?? string.Empty
            );
        }

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

        return new ZipArchive(new MemoryStream(zipBytes), ZipArchiveMode.Read);
    }

    /// <summary>
    /// The reporting person whose position represents the filing: the one whose
    /// CIK matches the lead filer, else the one reporting the largest aggregate
    /// (the controlling/parent entity that beneficially owns the whole group's
    /// stake). Null when the filing lists no reporting persons.
    /// </summary>
    internal static Parsed13DGReportingPerson SelectLeadPerson(Parsed13DGFiling filing)
    {
        if (filing.ReportingPersons.Count == 0)
            return null;

        var byFilerCik = filing.ReportingPersons.FirstOrDefault(p =>
            !string.IsNullOrEmpty(p.Cik)
            && !string.IsNullOrEmpty(filing.FilerCik)
            && p.Cik == filing.FilerCik
        );

        return byFilerCik
            ?? filing.ReportingPersons.OrderByDescending(p => p.AggregateAmountOwned).First();
    }

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
    /// The TSV reader splits on tabs and newlines; any embedded in a free-text
    /// field (names, titles) would corrupt the row. Replace them with spaces —
    /// the import pipeline trims values anyway.
    /// </summary>
    private static string Clean(string value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;
        return value.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
    }
}
