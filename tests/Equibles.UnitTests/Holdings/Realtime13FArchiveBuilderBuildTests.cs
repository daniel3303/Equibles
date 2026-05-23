using System.IO.Compression;
using Equibles.Holdings.HostedService.Models;
using Equibles.Holdings.HostedService.Services;

namespace Equibles.UnitTests.Holdings;

// Lane B (coverage): exercises the full Build path — the entire class is
// zero-hit today. A single filing with one holding and one other manager
// drives through TSV header construction, the filing/holdings/otherManagers
// loops, the Clean sanitizer, ZIP creation, and entry writing.
public class Realtime13FArchiveBuilderBuildTests
{
    private readonly Realtime13FArchiveBuilder _sut = new();

    [Fact]
    public void Build_SingleFilingWithHoldingAndOtherManager_ProducesFourTsvEntriesWithCorrectContent()
    {
        var filing = new Parsed13FFiling
        {
            AccessionNumber = "0000950123-24-007578",
            Cik = "1067983",
            FilingDate = new DateOnly(2024, 5, 15),
            PeriodOfReport = new DateOnly(2024, 3, 31),
            IsAmendment = false,
            FilingManagerName = "BERKSHIRE HATHAWAY INC",
            City = "OMAHA",
            StateOrCountry = "NE",
            Form13FFileNumber = "028-00338",
            CrdNumber = "314159",
            Holdings =
            {
                new Parsed13FHolding
                {
                    Cusip = "037833100",
                    TitleOfClass = "COM",
                    ShareType = "SH",
                    Shares = 915560,
                    PutCall = "",
                    InvestmentDiscretion = "SOLE",
                    VotingAuthSole = 800000,
                    VotingAuthShared = 100000,
                    VotingAuthNone = 15560,
                    OtherManagerNumber = 1,
                },
            },
            OtherManagers = { [1] = "General Re-New England Asset Mgmt" },
        };

        using var archive = _sut.Build([filing]);

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

        var submission = ReadEntry(archive, "SUBMISSION.tsv");
        submission.Should().Contain("13F-HR");
        submission.Should().Contain("0000950123-24-007578");
        submission.Should().Contain("2024-05-15");
        submission.Should().Contain("1067983");

        var coverPage = ReadEntry(archive, "COVERPAGE.tsv");
        coverPage.Should().Contain("BERKSHIRE HATHAWAY INC");
        coverPage.Should().Contain("OMAHA");
        coverPage.Should().Contain("N"); // IsAmendment = false

        var infoTable = ReadEntry(archive, "INFOTABLE.tsv");
        infoTable.Should().Contain("037833100");
        infoTable.Should().Contain("SH");
        infoTable.Should().Contain("915560");

        var otherMgr = ReadEntry(archive, "OTHERMANAGER2.tsv");
        otherMgr.Should().Contain("General Re-New England Asset Mgmt");
    }

    private static string ReadEntry(ZipArchive archive, string name)
    {
        var entry = archive.GetEntry(name);
        using var stream = entry.Open();
        using var reader = new System.IO.StreamReader(stream);
        return reader.ReadToEnd();
    }
}
