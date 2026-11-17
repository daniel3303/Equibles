using System.IO.Compression;
using Equibles.Holdings.HostedService.Extensions;
using Equibles.Holdings.HostedService.Services;

namespace Equibles.UnitTests.Holdings;

// Rows in the layout of SEC's 13F data sets. The first report is Norges Bank's 2026-03-31
// original as filed (TestAssets/Holdings/13f-confidential-treatment-submission.txt): it declares
// 1,507 positions and publishes one placeholder row.
public class Form13FConfidentialFilingReaderTests
{
    private const string Submissions = """
        ACCESSION_NUMBER	FILING_DATE	SUBMISSIONTYPE	CIK	PERIODOFREPORT
        0001374170-26-000023	11-MAY-2026	13F-HR	0001374170	31-MAR-2026
        0000000002-26-000001	14-MAY-2026	13F-HR/A	0000000002	31-MAR-2026
        0000000003-26-000001	14-MAY-2026	13F-HR	0000000003	31-MAR-2026
        0000000004-26-000001	14-MAY-2026	13F-NT	0000000004	31-MAR-2026
        """;

    private const string SummaryPages = """
        ACCESSION_NUMBER	OTHERINCLUDEDMANAGERSCOUNT	TABLEENTRYTOTAL	TABLEVALUETOTAL	ISCONFIDENTIALOMITTED
        0001374170-26-000023	0	1507	864690921985	Y
        0000000002-26-000001	0	3	900	Y
        0000000003-26-000001	0	1	500	N
        """;

    private const string InfoTable = """
        ACCESSION_NUMBER	INFOTABLE_SK	NAMEOFISSUER	CUSIP	VALUE	SSHPRNAMT	SSHPRNAMTTYPE
        0001374170-26-000023	1	NA	000000000	0	0	SH
        0000000002-26-000001	2	FIRST	111111111	400	10	SH
        0000000002-26-000001	3	SECOND	222222222	500	20	SH
        0000000002-26-000001	4	EXITED	333333333	0	0	SH
        0000000003-26-000001	5	THIRD	444444444	500	30	SH
        """;

    [Fact]
    public async Task Read_KeepsOnlyReportsThatDeclareOmittedPositions_WithWhatTheyStillDisclose()
    {
        using var archive = Archive(Submissions, SummaryPages, InfoTable);

        var result = await Form13FConfidentialFilingReader.Read(archive, new TsvParser());

        result.ColumnSeen.Should().BeTrue();
        var filings = result.Filings.ToDictionary(filing => filing.AccessionNumber);
        filings.Keys.Should().BeEquivalentTo("0001374170-26-000023", "0000000002-26-000001");
        var withheld = filings["0001374170-26-000023"];
        withheld.FilerCik.Should().Be("1374170");
        withheld.ReportDate.Should().Be(new DateOnly(2026, 3, 31));
        withheld.FilingDate.Should().Be(new DateOnly(2026, 5, 11));
        withheld.IsAmendment.Should().BeFalse();
        withheld.DeclaredEntryTotal.Should().Be(1507);
        withheld
            .DisclosedPositionCount.Should()
            .Be(0, "its only row is a placeholder without a quantity or a value");
        var partial = filings["0000000002-26-000001"];
        partial.IsAmendment.Should().BeTrue();
        partial
            .DisclosedPositionCount.Should()
            .Be(2, "a report that omits some positions still discloses the others");
    }

    [Fact]
    public async Task Read_SummaryPageWithoutTheOmissionColumn_SaysTheColumnWasNotSeen()
    {
        using var archive = Archive(
            Submissions,
            SummaryPages.Replace("ISCONFIDENTIALOMITTED", "RENAMED"),
            InfoTable
        );

        var result = await Form13FConfidentialFilingReader.Read(archive, new TsvParser());

        result.Filings.Should().BeEmpty();
        result.ColumnSeen.Should().BeFalse();
    }

    [Fact]
    public async Task Read_ConfidentialReportWithoutAnInformationTable_Throws()
    {
        using var archive = Archive(Submissions, SummaryPages, null);

        var read = () => Form13FConfidentialFilingReader.Read(archive, new TsvParser());

        // Zero disclosed positions would read as a withheld book.
        await read.Should().ThrowAsync<InvalidDataException>();
    }

    internal static ZipArchive Archive(string submissions, string summaryPages, string infoTable)
    {
        var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (
                var (table, text) in new[]
                {
                    ("SUBMISSION.tsv", submissions),
                    ("SUMMARYPAGE.tsv", summaryPages),
                    ("INFOTABLE.tsv", infoTable),
                }
            )
            {
                if (text == null)
                    continue;
                using var writer = new StreamWriter(archive.CreateEntry(table).Open());
                writer.Write(text);
            }
        }

        buffer.Position = 0;
        return new ZipArchive(buffer, ZipArchiveMode.Read);
    }
}
