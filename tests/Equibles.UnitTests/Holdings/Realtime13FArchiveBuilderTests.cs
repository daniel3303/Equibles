using System.IO.Compression;
using Equibles.Holdings.HostedService.Models;
using Equibles.Holdings.HostedService.Services;

namespace Equibles.UnitTests.Holdings;

public class Realtime13FArchiveBuilderTests
{
    private readonly Realtime13FArchiveBuilder _sut = new();

    [Fact]
    public void Build_EmptyCollection_ProducesArchiveWithFourEntries()
    {
        using var archive = _sut.Build([]);

        archive.Entries.Should().HaveCount(4);
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

    [Fact]
    public void Build_SingleFiling_ProducesCorrectEntryNames()
    {
        using var archive = _sut.Build([CreateFiling()]);

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

    [Fact]
    public void Build_SingleFiling_SubmissionHasCorrectHeaders()
    {
        using var archive = _sut.Build([CreateFiling()]);
        var content = ReadEntry(archive, "SUBMISSION.tsv");
        var headerLine = content.Split('\n')[0];

        headerLine
            .Should()
            .Be("SUBMISSIONTYPE\tACCESSION_NUMBER\tFILING_DATE\tPERIODOFREPORT\tCIK");
    }

    [Fact]
    public void Build_NonAmendment_SubmissionTypeIs13FHR()
    {
        var filing = CreateFiling(isAmendment: false);
        using var archive = _sut.Build([filing]);
        var content = ReadEntry(archive, "SUBMISSION.tsv");

        var dataLine = content.Split('\n')[1];
        dataLine.Should().StartWith("13F-HR\t");
    }

    [Fact]
    public void Build_Amendment_SubmissionTypeIs13FHRA()
    {
        var filing = CreateFiling(isAmendment: true);
        using var archive = _sut.Build([filing]);
        var content = ReadEntry(archive, "SUBMISSION.tsv");

        var dataLine = content.Split('\n')[1];
        dataLine.Should().StartWith("13F-HR/A\t");
    }

    [Fact]
    public void Build_NonAmendment_CoverPageIsAmendmentIsN()
    {
        var filing = CreateFiling(isAmendment: false);
        using var archive = _sut.Build([filing]);
        var content = ReadEntry(archive, "COVERPAGE.tsv");
        var dataLine = content.Split('\n')[1];
        var fields = dataLine.Split('\t');

        // ISAMENDMENT is the second field (index 1)
        fields[1].Should().Be("N");
    }

    [Fact]
    public void Build_Amendment_CoverPageIsAmendmentIsY()
    {
        var filing = CreateFiling(isAmendment: true);
        using var archive = _sut.Build([filing]);
        var content = ReadEntry(archive, "COVERPAGE.tsv");
        var dataLine = content.Split('\n')[1];
        var fields = dataLine.Split('\t');

        fields[1].Should().Be("Y");
    }

    [Fact]
    public void Build_WithHoldings_InfoTableContainsHoldingRows()
    {
        var filing = CreateFiling();
        filing.Holdings.Add(
            new Parsed13FHolding
            {
                Cusip = "037833100",
                TitleOfClass = "COM",
                ShareType = "SH",
                Shares = 915560,
                InvestmentDiscretion = "SOLE",
                VotingAuthSole = 800000,
                VotingAuthShared = 100000,
                VotingAuthNone = 15560,
            }
        );

        using var archive = _sut.Build([filing]);
        var content = ReadEntry(archive, "INFOTABLE.tsv");
        var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        // Header + 1 data row
        lines.Should().HaveCount(2);
        lines[1].Should().Contain("037833100");
        lines[1].Should().Contain("915560");
    }

    [Fact]
    public void Build_HoldingWithOtherManager_OtherManagerNumberWritten()
    {
        var filing = CreateFiling();
        filing.Holdings.Add(
            new Parsed13FHolding
            {
                Cusip = "037833100",
                TitleOfClass = "COM",
                ShareType = "SH",
                Shares = 1000,
                InvestmentDiscretion = "SOLE",
                OtherManagerNumber = 3,
            }
        );

        using var archive = _sut.Build([filing]);
        var content = ReadEntry(archive, "INFOTABLE.tsv");
        var dataLine = content.Split('\n')[1];
        var fields = dataLine.Split('\t');

        // OTHERMANAGER is field index 9 (0-based)
        fields[9].Should().Be("3");
    }

    [Fact]
    public void Build_HoldingWithoutOtherManager_OtherManagerNumberEmpty()
    {
        var filing = CreateFiling();
        filing.Holdings.Add(
            new Parsed13FHolding
            {
                Cusip = "037833100",
                TitleOfClass = "COM",
                ShareType = "SH",
                Shares = 1000,
                InvestmentDiscretion = "SOLE",
                OtherManagerNumber = null,
            }
        );

        using var archive = _sut.Build([filing]);
        var content = ReadEntry(archive, "INFOTABLE.tsv");
        var dataLine = content.Split('\n')[1];
        var fields = dataLine.Split('\t');

        fields[9].Should().BeEmpty();
    }

    [Fact]
    public void Build_WithOtherManagers_WritesManagerRows()
    {
        var filing = CreateFiling();
        filing.OtherManagers[1] = "Manager Alpha";
        filing.OtherManagers[2] = "Manager Beta";

        using var archive = _sut.Build([filing]);
        var content = ReadEntry(archive, "OTHERMANAGER2.tsv");
        var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        // Header + 2 manager rows
        lines.Should().HaveCount(3);
        content.Should().Contain("Manager Alpha");
        content.Should().Contain("Manager Beta");
    }

    [Fact]
    public void Build_NoOtherManagers_OnlyHeaders()
    {
        var filing = CreateFiling();
        filing.OtherManagers.Clear();

        using var archive = _sut.Build([filing]);
        var content = ReadEntry(archive, "OTHERMANAGER2.tsv");
        var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        // Only the header row
        lines.Should().HaveCount(1);
        lines[0].Should().StartWith("ACCESSION_NUMBER");
    }

    [Fact]
    public void Build_ManagerNameWithTabs_TabsReplacedWithSpaces()
    {
        var filing = CreateFiling();
        filing.FilingManagerName = "ACME\tFund\tManagement";

        using var archive = _sut.Build([filing]);
        var content = ReadEntry(archive, "COVERPAGE.tsv");
        var dataLine = content.Split('\n')[1];
        var fields = dataLine.Split('\t');

        // Manager name is field index 3 (after ACCESSION_NUMBER, ISAMENDMENT, AMENDMENTTYPE)
        fields[3].Should().Be("ACME Fund Management");
    }

    [Fact]
    public void Build_ManagerNameWithNewlines_NewlinesReplacedWithSpaces()
    {
        var filing = CreateFiling();
        filing.FilingManagerName = "ACME\nFund\rManagement";

        using var archive = _sut.Build([filing]);
        var content = ReadEntry(archive, "COVERPAGE.tsv");
        var dataLine = content.Split('\n')[1];
        var fields = dataLine.Split('\t');

        fields[3].Should().Be("ACME Fund Management");
    }

    [Fact]
    public void Build_MultipleFilings_AllRowsPresent()
    {
        var filing1 = CreateFiling(accession: "0000000001-24-000001");
        var filing2 = CreateFiling(accession: "0000000001-24-000002");
        filing1.Holdings.Add(
            new Parsed13FHolding
            {
                Cusip = "111111111",
                ShareType = "SH",
                Shares = 100,
                InvestmentDiscretion = "SOLE",
            }
        );
        filing2.Holdings.Add(
            new Parsed13FHolding
            {
                Cusip = "222222222",
                ShareType = "SH",
                Shares = 200,
                InvestmentDiscretion = "SOLE",
            }
        );

        using var archive = _sut.Build([filing1, filing2]);

        var submission = ReadEntry(archive, "SUBMISSION.tsv");
        var submissionLines = submission.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        submissionLines.Should().HaveCount(3); // header + 2 data rows

        var infoTable = ReadEntry(archive, "INFOTABLE.tsv");
        var infoLines = infoTable.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        infoLines.Should().HaveCount(3); // header + 2 holding rows
        infoTable.Should().Contain("111111111");
        infoTable.Should().Contain("222222222");
    }

    [Fact]
    public void Build_DatesFormattedAsIso8601()
    {
        var filing = CreateFiling();
        filing.FilingDate = new DateOnly(2024, 3, 15);
        filing.PeriodOfReport = new DateOnly(2024, 3, 31);

        using var archive = _sut.Build([filing]);
        var content = ReadEntry(archive, "SUBMISSION.tsv");

        content.Should().Contain("2024-03-15");
        content.Should().Contain("2024-03-31");
    }

    [Fact]
    public void Build_TabCountConsistentAcrossRows()
    {
        var filing = CreateFiling();
        filing.Holdings.Add(
            new Parsed13FHolding
            {
                Cusip = "037833100",
                TitleOfClass = "COM",
                ShareType = "SH",
                Shares = 1000,
                InvestmentDiscretion = "SOLE",
            }
        );
        filing.OtherManagers[1] = "Test Manager";

        using var archive = _sut.Build([filing]);

        // Check each TSV file: all lines (including header) should have the same tab count
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
            var content = ReadEntry(archive, entryName);
            var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            var expectedTabs = lines[0].Count(c => c == '\t');

            foreach (var line in lines)
            {
                line.Count(c => c == '\t')
                    .Should()
                    .Be(
                        expectedTabs,
                        $"every row in {entryName} should have {expectedTabs} tab(s)"
                    );
            }
        }
    }

    private static Parsed13FFiling CreateFiling(
        bool isAmendment = false,
        string accession = "0000000001-24-000001"
    )
    {
        return new Parsed13FFiling
        {
            AccessionNumber = accession,
            Cik = "1234567",
            FilingDate = new DateOnly(2024, 5, 15),
            PeriodOfReport = new DateOnly(2024, 3, 31),
            IsAmendment = isAmendment,
            FilingManagerName = "TEST FUND LLC",
            City = "NEW YORK",
            StateOrCountry = "NY",
            Form13FFileNumber = "028-12345",
            CrdNumber = "999999",
        };
    }

    private static string ReadEntry(ZipArchive archive, string entryName)
    {
        var entry = archive.GetEntry(entryName);
        entry.Should().NotBeNull($"entry '{entryName}' should exist in the archive");
        using var reader = new StreamReader(entry!.Open());
        return reader.ReadToEnd();
    }
}
