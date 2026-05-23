using System.IO.Compression;
using Equibles.Holdings.HostedService.Models;
using Equibles.Holdings.HostedService.Services;

namespace Equibles.UnitTests.Holdings;

public class Realtime13FArchiveBuilderTests
{
    private readonly Realtime13FArchiveBuilder _sut = new();

    private static Parsed13FFiling CreateFiling(
        string accession = "0000000000-24-000001",
        string cik = "1234567",
        DateOnly? filingDate = null,
        DateOnly? periodOfReport = null,
        bool isAmendment = false,
        string filingManagerName = "TEST FUND",
        string city = "NEW YORK",
        string stateOrCountry = "NY",
        string form13FFileNumber = "028-12345",
        string crdNumber = "999999"
    ) =>
        new()
        {
            AccessionNumber = accession,
            Cik = cik,
            FilingDate = filingDate ?? new DateOnly(2024, 6, 15),
            PeriodOfReport = periodOfReport ?? new DateOnly(2024, 6, 30),
            IsAmendment = isAmendment,
            FilingManagerName = filingManagerName,
            City = city,
            StateOrCountry = stateOrCountry,
            Form13FFileNumber = form13FFileNumber,
            CrdNumber = crdNumber,
        };

    private static Parsed13FHolding CreateHolding(
        string cusip = "037833100",
        string titleOfClass = "AAPL INC",
        string shareType = "SH",
        long shares = 50000,
        string putCall = "",
        string investmentDiscretion = "SOLE",
        long votingAuthSole = 50000,
        long votingAuthShared = 0,
        long votingAuthNone = 0,
        int? otherManagerNumber = null
    ) =>
        new()
        {
            Cusip = cusip,
            TitleOfClass = titleOfClass,
            ShareType = shareType,
            Shares = shares,
            PutCall = putCall,
            InvestmentDiscretion = investmentDiscretion,
            VotingAuthSole = votingAuthSole,
            VotingAuthShared = votingAuthShared,
            VotingAuthNone = votingAuthNone,
            OtherManagerNumber = otherManagerNumber,
        };

    private static string ReadEntry(ZipArchive archive, string entryName)
    {
        var entry = archive.GetEntry(entryName);
        entry.Should().NotBeNull($"archive should contain entry '{entryName}'");
        using var reader = new StreamReader(entry!.Open());
        return reader.ReadToEnd();
    }

    private static string[] Lines(string tsv) =>
        tsv.Split('\n', StringSplitOptions.RemoveEmptyEntries);

    // ── Archive structure ────────────────────────────────────────────

    [Fact]
    public void Build_EmptyCollection_ProducesArchiveWithFourEntries()
    {
        using var archive = _sut.Build([]);

        archive.Entries.Should().HaveCount(4);
    }

    [Fact]
    public void Build_SingleFiling_ProducesCorrectEntryNames()
    {
        var filing = CreateFiling();

        using var archive = _sut.Build([filing]);

        archive
            .Entries.Select(e => e.Name)
            .Should()
            .BeEquivalentTo([
                "SUBMISSION.tsv",
                "COVERPAGE.tsv",
                "INFOTABLE.tsv",
                "OTHERMANAGER2.tsv",
            ]);
    }

    // ── SUBMISSION.tsv ───────────────────────────────────────────────

    [Fact]
    public void Build_SingleFiling_SubmissionHasCorrectHeaders()
    {
        using var archive = _sut.Build([]);

        var tsv = ReadEntry(archive, "SUBMISSION.tsv");

        tsv.Should()
            .StartWith("SUBMISSIONTYPE\tACCESSION_NUMBER\tFILING_DATE\tPERIODOFREPORT\tCIK\n");
    }

    [Fact]
    public void Build_NonAmendment_SubmissionTypeIs13FHR()
    {
        var filing = CreateFiling(isAmendment: false);

        using var archive = _sut.Build([filing]);

        var lines = Lines(ReadEntry(archive, "SUBMISSION.tsv"));
        lines.Should().HaveCount(2);
        lines[1].Split('\t')[0].Should().Be("13F-HR");
    }

    [Fact]
    public void Build_Amendment_SubmissionTypeIs13FHRA()
    {
        var filing = CreateFiling(isAmendment: true);

        using var archive = _sut.Build([filing]);

        var lines = Lines(ReadEntry(archive, "SUBMISSION.tsv"));
        lines[1].Split('\t')[0].Should().Be("13F-HR/A");
    }

    // ── COVERPAGE.tsv ────────────────────────────────────────────────

    [Fact]
    public void Build_NonAmendment_CoverPageIsAmendmentIsN()
    {
        var filing = CreateFiling(isAmendment: false);

        using var archive = _sut.Build([filing]);

        var lines = Lines(ReadEntry(archive, "COVERPAGE.tsv"));
        lines.Should().HaveCountGreaterThanOrEqualTo(2);
        var fields = lines[1].Split('\t');
        fields[1].Should().Be("N");
    }

    [Fact]
    public void Build_Amendment_CoverPageIsAmendmentIsY()
    {
        var filing = CreateFiling(isAmendment: true);

        using var archive = _sut.Build([filing]);

        var lines = Lines(ReadEntry(archive, "COVERPAGE.tsv"));
        var fields = lines[1].Split('\t');
        fields[1].Should().Be("Y");
    }

    // ── INFOTABLE.tsv ────────────────────────────────────────────────

    [Fact]
    public void Build_WithHoldings_InfoTableContainsHoldingRows()
    {
        var filing = CreateFiling();
        filing.Holdings.Add(
            CreateHolding(
                cusip: "037833100",
                shares: 75000,
                votingAuthSole: 75000,
                votingAuthShared: 100,
                votingAuthNone: 200
            )
        );

        using var archive = _sut.Build([filing]);

        var lines = Lines(ReadEntry(archive, "INFOTABLE.tsv"));
        lines.Should().HaveCount(2);

        var fields = lines[1].Split('\t');
        fields[1].Should().Be("037833100");
        fields[4].Should().Be("75000");
        fields[5].Should().Be("75000");
        fields[6].Should().Be("100");
        fields[7].Should().Be("200");
    }

    [Fact]
    public void Build_HoldingWithOtherManager_OtherManagerNumberWritten()
    {
        var filing = CreateFiling();
        filing.Holdings.Add(CreateHolding(otherManagerNumber: 7));

        using var archive = _sut.Build([filing]);

        var lines = Lines(ReadEntry(archive, "INFOTABLE.tsv"));
        var fields = lines[1].Split('\t');
        fields[9].Should().Be("7");
    }

    [Fact]
    public void Build_HoldingWithoutOtherManager_OtherManagerNumberEmpty()
    {
        var filing = CreateFiling();
        filing.Holdings.Add(CreateHolding(otherManagerNumber: null));

        using var archive = _sut.Build([filing]);

        var lines = Lines(ReadEntry(archive, "INFOTABLE.tsv"));
        var fields = lines[1].Split('\t');
        fields[9].Should().BeEmpty();
    }

    // ── OTHERMANAGER2.tsv ────────────────────────────────────────────

    [Fact]
    public void Build_WithOtherManagers_WritesManagerRows()
    {
        var filing = CreateFiling();
        filing.OtherManagers = new Dictionary<int, string>
        {
            { 1, "MANAGER ONE" },
            { 2, "MANAGER TWO" },
        };

        using var archive = _sut.Build([filing]);

        var lines = Lines(ReadEntry(archive, "OTHERMANAGER2.tsv"));
        lines.Should().HaveCount(3);

        var row1 = lines[1].Split('\t');
        row1[0].Should().Be(filing.AccessionNumber);
        row1[1].Should().Be("1");
        row1[2].Should().Be("MANAGER ONE");

        var row2 = lines[2].Split('\t');
        row2[1].Should().Be("2");
        row2[2].Should().Be("MANAGER TWO");
    }

    [Fact]
    public void Build_NoOtherManagers_OnlyHeaders()
    {
        var filing = CreateFiling();

        using var archive = _sut.Build([filing]);

        var lines = Lines(ReadEntry(archive, "OTHERMANAGER2.tsv"));
        lines.Should().HaveCount(1);
        lines[0].Should().StartWith("ACCESSION_NUMBER");
    }

    // ── Clean / sanitization ─────────────────────────────────────────

    [Fact]
    public void Build_ManagerNameWithTabs_TabsReplacedWithSpaces()
    {
        var filing = CreateFiling(filingManagerName: "FUND\tMANAGER");

        using var archive = _sut.Build([filing]);

        var coverPage = ReadEntry(archive, "COVERPAGE.tsv");
        coverPage.Should().Contain("FUND MANAGER");
        var dataLine = Lines(coverPage)[1];
        dataLine.Split('\t').Should().HaveCount(Lines(coverPage)[0].Split('\t').Length);
    }

    [Fact]
    public void Build_ManagerNameWithNewlines_NewlinesReplacedWithSpaces()
    {
        var filing = CreateFiling(filingManagerName: "FUND\nMANAGER\rLLC");

        using var archive = _sut.Build([filing]);

        var coverPage = ReadEntry(archive, "COVERPAGE.tsv");
        coverPage.Should().Contain("FUND MANAGER LLC");
    }

    // ── Multiple filings ─────────────────────────────────────────────

    [Fact]
    public void Build_MultipleFilings_AllRowsPresent()
    {
        var filing1 = CreateFiling(accession: "0000000000-24-000001", cik: "1111111");
        var filing2 = CreateFiling(accession: "0000000000-24-000002", cik: "2222222");

        using var archive = _sut.Build([filing1, filing2]);

        var submissionLines = Lines(ReadEntry(archive, "SUBMISSION.tsv"));
        submissionLines.Should().HaveCount(3);

        var coverLines = Lines(ReadEntry(archive, "COVERPAGE.tsv"));
        coverLines.Should().HaveCount(3);
    }

    // ── Date formatting ──────────────────────────────────────────────

    [Fact]
    public void Build_DatesFormattedAsIso8601()
    {
        var filing = CreateFiling(
            filingDate: new DateOnly(2025, 1, 7),
            periodOfReport: new DateOnly(2024, 12, 31)
        );

        using var archive = _sut.Build([filing]);

        var lines = Lines(ReadEntry(archive, "SUBMISSION.tsv"));
        var fields = lines[1].Split('\t');
        fields[2].Should().Be("2025-01-07");
        fields[3].Should().Be("2024-12-31");
    }

    // ── TSV integrity ────────────────────────────────────────────────

    [Fact]
    public void Build_TabCountConsistentAcrossRows()
    {
        var filing = CreateFiling();
        filing.Holdings.Add(CreateHolding());
        filing.Holdings.Add(CreateHolding(cusip: "594918104", titleOfClass: "MSFT"));
        filing.OtherManagers = new Dictionary<int, string> { { 1, "CO-MGR" } };

        using var archive = _sut.Build([filing]);

        foreach (
            var entryName in new[]
            {
                "SUBMISSION.tsv",
                "COVERPAGE.tsv",
                "INFOTABLE.tsv",
                "OTHERMANAGER2.tsv",
            }
        )
        {
            var lines = Lines(ReadEntry(archive, entryName));
            var headerTabCount = lines[0].Count(c => c == '\t');

            foreach (var line in lines.Skip(1))
            {
                line.Count(c => c == '\t')
                    .Should()
                    .Be(headerTabCount, $"row in {entryName} should have same tab count as header");
            }
        }
    }
}
