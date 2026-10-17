using System.IO.Compression;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.HostedService.Extensions;
using Equibles.Holdings.HostedService.Services;

namespace Equibles.UnitTests.Holdings;

// Real rows from SEC's 01jun2026-31aug2026 13F data set: four notices (one an amendment), a
// combination 13F-HR with its own other-manager list, and a plain 13F-HR. EDGAR's own notices
// list Simmons Bank before King Luther and Boston Partners first for ORIX, which is ascending
// OTHERMANAGER_SK order, not file order.
public class Form13FNoticeReaderTests
{
    private const string DataSet = "01jun2026-31aug2026_form13f.zip";

    private static ZipArchive FixtureArchive(Func<string, string, string> edit = null)
    {
        var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var table in new[] { "SUBMISSION.tsv", "COVERPAGE.tsv", "OTHERMANAGER.tsv" })
            {
                var text = File.ReadAllText(
                    Path.Combine(
                        AppContext.BaseDirectory,
                        "TestAssets",
                        "Holdings",
                        "13f-notices",
                        table
                    )
                );
                using var writer = new StreamWriter(archive.CreateEntry(table).Open());
                writer.Write(edit == null ? text : edit(table, text));
            }
        }

        buffer.Position = 0;
        return new ZipArchive(buffer, ZipArchiveMode.Read);
    }

    private static async Task<Dictionary<string, Form13FNotice>> ReadFixture()
    {
        using var archive = FixtureArchive();
        return (await Form13FNoticeReader.Read(archive, DataSet, new TsvParser())).ToDictionary(
            notice => notice.AccessionNumber
        );
    }

    [Fact]
    public async Task Read_KeepsOnlyNoticesAndTheirAmendments()
    {
        var notices = await ReadFixture();

        notices
            .Keys.Should()
            .BeEquivalentTo(
                "0001193125-26-328872",
                "0001034180-26-000010",
                "0001911876-26-000020",
                "0001257165-26-000005"
            );
        notices["0001911876-26-000020"].IsAmendment.Should().BeTrue();
        notices["0001193125-26-328872"].IsAmendment.Should().BeFalse();
    }

    [Fact]
    public async Task Read_TakesTheFilerFromTheSubmissionAndCoverPage()
    {
        var simmons = (await ReadFixture())["0001193125-26-328872"];

        simmons.FilerCik.Should().Be("90498");
        simmons.FilerName.Should().Be("SIMMONS FIRST NATIONAL CORP");
        simmons.ReportDate.Should().Be(new DateOnly(2026, 6, 30));
        simmons.FilingDate.Should().Be(new DateOnly(2026, 7, 31));
        simmons.Form13FFileNumber.Should().Be("028-04801");
        simmons.CrdNumber.Should().BeNull();
        simmons.DataSetFileName.Should().Be(DataSet);
    }

    [Fact]
    public async Task Read_ListsReportersInFiledOrderWithTheirFiledIdentifiers()
    {
        var notices = await ReadFixture();

        notices["0001193125-26-328872"]
            .Reporters.Select(r => (r.Ordinal, r.Name, r.Cik, r.Form13FFileNumber))
            .Should()
            .Equal(
                (1, "Simmons Bank", null, "28-17448"),
                (2, "King Luther Capital Management Corp", null, "28-01071")
            );
        notices["0001257165-26-000005"]
            .Reporters.Select(r => (r.Name, r.Cik))
            .Should()
            .Equal(
                ("Boston Partners", "1386060"),
                ("Robeco Institutional Asset Management B.V.", "1418773"),
                ("Robeco Schweiz AG", "1418427"),
                ("HARBOR CAPITAL ADVISORS, INC.", "1039128")
            );
    }

    [Fact]
    public async Task Read_IdentifiersWrittenIntoTheNameStayInTheName()
    {
        var reporter = (await ReadFixture())["0001034180-26-000010"].Reporters.Single();

        // Only filed identifier fields are joinable; nothing is read out of a name.
        reporter.Name.Should().Be("Markel Group Inc., File Number 028-06647, CIK 0001096343");
        reporter.Cik.Should().BeNull();
        reporter.Form13FFileNumber.Should().BeNull();
    }

    [Fact]
    public async Task Read_ArchiveWithoutSubmissions_IsUnreadable()
    {
        var buffer = new MemoryStream();
        using (var create = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
            create.CreateEntry("COVERPAGE.tsv");
        buffer.Position = 0;
        using var archive = new ZipArchive(buffer, ZipArchiveMode.Read);

        var read = () => Form13FNoticeReader.Read(archive, DataSet, new TsvParser());

        await read.Should().ThrowAsync<InvalidDataException>();
    }

    [Theory]
    [InlineData("13F-NT")]
    [InlineData("13F-NT/A")]
    public void NoticeForms_AreNeverHoldingsFilings(string formType)
    {
        // A notice filer reports no holdings; mapping it would make it a 13F filer everywhere.
        formType.ToHoldingsFilingType().Should().BeNull();
    }
}
