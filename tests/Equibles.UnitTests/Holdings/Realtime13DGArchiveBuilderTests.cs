using System.IO.Compression;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.HostedService.Models;
using Equibles.Holdings.HostedService.Services;

namespace Equibles.UnitTests.Holdings;

// Pins the 13D/13G -> TSV projection: a filing lists overlapping reporting
// persons, and the builder must attribute ONE non-additive position to the
// lead filer, carrying percent-of-class through the synthetic INFOTABLE column.
public class Realtime13DGArchiveBuilderTests
{
    private static Parsed13DGReportingPerson Person(
        string cik,
        long aggregate,
        decimal percent,
        long soleVote = 0,
        long sharedVote = 0
    ) =>
        new()
        {
            Cik = cik,
            Name = cik == null ? "No-CIK Person" : $"Person {cik}",
            AggregateAmountOwned = aggregate,
            PercentOfClass = percent,
            SoleVotingPower = soleVote,
            SharedVotingPower = sharedVote,
        };

    [Fact]
    public void SelectLeadPerson_FilerCikMatches_PrefersThatPersonOverLargerAggregate()
    {
        var filing = new Parsed13DGFiling
        {
            FilerCik = "2059583",
            ReportingPersons = [Person(null, 99_999, 9.9m), Person("2059583", 32_671_542, 6.3m)],
        };

        var lead = Realtime13DGArchiveBuilder.SelectLeadPerson(filing);

        lead.Cik.Should().Be("2059583");
        lead.PercentOfClass.Should().Be(6.3m);
    }

    [Fact]
    public void SelectLeadPerson_NoFilerCikMatch_FallsBackToLargestAggregate()
    {
        var filing = new Parsed13DGFiling
        {
            FilerCik = "1423053",
            ReportingPersons =
            [
                Person(null, 1_000, 0.1m),
                Person(null, 1_624_818, 9.9m),
                Person(null, 59, 0.0m),
            ],
        };

        var lead = Realtime13DGArchiveBuilder.SelectLeadPerson(filing);

        lead.AggregateAmountOwned.Should().Be(1_624_818);
        lead.PercentOfClass.Should().Be(9.9m);
    }

    [Fact]
    public void SelectLeadPerson_NoReportingPersons_ReturnsNull()
    {
        var filing = new Parsed13DGFiling { FilerCik = "1", ReportingPersons = [] };

        Realtime13DGArchiveBuilder.SelectLeadPerson(filing).Should().BeNull();
    }

    [Fact]
    public void Build_ProjectsLeadPositionIntoInfoTableWithPercentOfClass()
    {
        var filing = new Parsed13DGFiling
        {
            AccessionNumber = "0001140361-25-017533",
            SubmissionType = "SCHEDULE 13D",
            FilingType = FilingType.Schedule13D,
            IsAmendment = false,
            FilerCik = "2059583",
            FilingDate = new DateOnly(2025, 5, 6),
            DateOfEvent = new DateOnly(2025, 4, 29),
            IssuerCusip = "82846H405",
            SecuritiesClassTitle = "Common Stock",
            ReportingPersons =
            [
                Person("2059583", 32_671_542, 6.3m, soleVote: 0, sharedVote: 32_671_542),
            ],
        };

        using var archive = new Realtime13DGArchiveBuilder().Build([filing]);

        var infoTable = ReadEntry(archive, "INFOTABLE.tsv");
        var row = DataRow(infoTable);
        var columns = SplitColumns(infoTable);

        // CUSIP, shares (aggregate), shared voting, and percent-of-class land in
        // their columns; value is 0 (price map derives it later).
        row[Index(columns, "CUSIP")].Should().Be("82846H405");
        row[Index(columns, "SSHPRNAMT")].Should().Be("32671542");
        row[Index(columns, "VOTING_AUTH_SHARED")].Should().Be("32671542");
        row[Index(columns, "VALUE")].Should().Be("0");
        row[Index(columns, "PERCENTOFCLASS")].Should().Be("6.3");

        var submission = ReadEntry(archive, "SUBMISSION.tsv");
        var subColumns = SplitColumns(submission);
        var subRow = DataRow(submission);
        // The event date becomes the report period; the form type is preserved.
        subRow[Index(subColumns, "PERIODOFREPORT")].Should().Be("2025-04-29");
        subRow[Index(subColumns, "SUBMISSIONTYPE")].Should().Be("SCHEDULE 13D");
    }

    private static string ReadEntry(ZipArchive archive, string name)
    {
        using var reader = new StreamReader(archive.GetEntry(name)!.Open());
        return reader.ReadToEnd();
    }

    private static string[] SplitColumns(string tsv) =>
        tsv.Split('\n')[0].TrimEnd('\r').Split('\t');

    private static string[] DataRow(string tsv) =>
        tsv.Split('\n', StringSplitOptions.RemoveEmptyEntries)[1].TrimEnd('\r').Split('\t');

    private static int Index(string[] columns, string name) => Array.IndexOf(columns, name);
}
