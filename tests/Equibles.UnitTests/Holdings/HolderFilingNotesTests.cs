using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Mcp.Helpers;
using FluentAssertions;
using Xunit;

namespace Equibles.UnitTests.Holdings;

public class HolderFilingNotesTests
{
    private static readonly IReadOnlyList<DateOnly> ReportDates =
    [
        new DateOnly(2026, 6, 30),
        new DateOnly(2026, 3, 31),
        new DateOnly(2025, 9, 30),
    ];

    [Fact]
    public void MissingQuarter_QuarterNewerThanTheFilersLatest_SaysNoneOnFileAndPointsToTheLatest()
    {
        var note = HolderFilingNotes.MissingQuarter(
            "TIGER GLOBAL MANAGEMENT LLC",
            new DateOnly(2026, 9, 30),
            new DateOnly(2026, 6, 30),
            ReportDates
        );

        note.Should()
            .Contain("TIGER GLOBAL MANAGEMENT LLC has no 13F for 2026-09-30 on file here")
            .And.Contain("its latest report, 2026-06-30")
            .And.Contain("GetLatest13FFilings");
        note.Should().NotContain("is not a 13F report date");
    }

    [Fact]
    public void MissingQuarter_QuarterTheFilerSkipped_NamesTheNearestEarlierReport()
    {
        var note = HolderFilingNotes.MissingQuarter(
            "Adviser",
            new DateOnly(2025, 12, 31),
            new DateOnly(2025, 9, 30),
            ReportDates
        );

        note.Should()
            .Contain("has no 13F for 2025-12-31 on file here")
            .And.Contain("the nearest report before it, 2025-09-30");
    }

    [Fact]
    public void MissingQuarter_MidQuarterDate_LeavesTheSharedSnapNote()
    {
        HolderFilingNotes
            .MissingQuarter(
                "Adviser",
                new DateOnly(2026, 8, 15),
                new DateOnly(2026, 6, 30),
                ReportDates
            )
            .Should()
            .BeNull();
    }

    [Fact]
    public void Filings_ListsEach13FNewestFirstWithFiledAndLoadedDates()
    {
        var quarter = new DateOnly(2026, 9, 30);
        var line = HolderFilingNotes.Filings(
            quarter,
            [
                Filing("0000919574-26-008062", new DateOnly(2026, 11, 16), amendment: false),
                Filing("0000919574-26-008400", new DateOnly(2026, 11, 20), amendment: true),
                // A Schedule 13G event stored on the quarter end is not the 13F behind the book.
                Filing(
                    "0000919574-26-009000",
                    new DateOnly(2026, 11, 25),
                    amendment: false,
                    FilingType.Schedule13G
                ),
            ]
        );

        line.Should()
            .Be(
                "13F filing for 2026-09-30: "
                    + "13F-HR/A filed 2026-11-20, last loaded 2026-11-17 12:42 UTC (accession 0000919574-26-008400); "
                    + "13F-HR filed 2026-11-16, last loaded 2026-11-17 12:42 UTC (accession 0000919574-26-008062)."
            );
    }

    [Fact]
    public void Filings_NoRollupForTheQuarter_AddsNoLine()
    {
        HolderFilingNotes.Filings(new DateOnly(2026, 9, 30), []).Should().BeNull();
    }

    private static InstitutionalFiling Filing(
        string accession,
        DateOnly filed,
        bool amendment,
        FilingType type = FilingType.Form13F
    ) =>
        new()
        {
            AccessionNumber = accession,
            FilingDate = filed,
            ReportDate = new DateOnly(2026, 9, 30),
            IsAmendment = amendment,
            FilingType = type,
            CreationTime = new DateTime(2026, 11, 17, 12, 42, 9, DateTimeKind.Utc),
        };
}
