using Equibles.Finra.Data.Calendars;

namespace Equibles.UnitTests.Finra;

/// <summary>
/// Pins <see cref="ShortInterestCalendar"/> against FINRA's own published reporting calendar
/// at https://www.finra.org/filing-reporting/regulatory-filing-systems/short-interest.
///
/// Every settlement/due/publication triple below is transcribed verbatim from that page (the
/// tail of the 2025 table it still lists, plus the whole of 2026). The calendar is derived
/// rather than scraped — FINRA answers a plain HTTP GET with 403 and only ever publishes the
/// current and next year — so this suite is what keeps the derivation honest: if FINRA ever
/// changes the rule, these rows fail instead of the dates quietly skewing.
/// </summary>
public class ShortInterestCalendarTests
{
    // FINRA's published calendar, "settlement, due, publication". Adding the next year's table
    // here as FINRA publishes it is the intended way to re-verify the derivation.
    private static readonly string[][] PublishedRows =
    [
        // 2025
        ["2025-11-14", "2025-11-18", "2025-11-25"],
        ["2025-11-28", "2025-12-02", "2025-12-09"],
        ["2025-12-15", "2025-12-17", "2025-12-24"],
        ["2025-12-31", "2026-01-05", "2026-01-12"],
        // 2026
        ["2026-01-15", "2026-01-20", "2026-01-27"],
        ["2026-01-30", "2026-02-03", "2026-02-10"],
        ["2026-02-13", "2026-02-18", "2026-02-25"],
        ["2026-02-27", "2026-03-03", "2026-03-10"],
        ["2026-03-13", "2026-03-17", "2026-03-24"],
        ["2026-03-31", "2026-04-02", "2026-04-10"],
        ["2026-04-15", "2026-04-17", "2026-04-24"],
        ["2026-04-30", "2026-05-04", "2026-05-11"],
        ["2026-05-15", "2026-05-19", "2026-05-27"],
        ["2026-05-29", "2026-06-02", "2026-06-09"],
        ["2026-06-15", "2026-06-17", "2026-06-25"],
        ["2026-06-30", "2026-07-02", "2026-07-10"],
        ["2026-07-15", "2026-07-17", "2026-07-24"],
        ["2026-07-31", "2026-08-04", "2026-08-11"],
        ["2026-08-14", "2026-08-18", "2026-08-25"],
        ["2026-08-31", "2026-09-02", "2026-09-10"],
        ["2026-09-15", "2026-09-17", "2026-09-24"],
        ["2026-09-30", "2026-10-02", "2026-10-09"],
        ["2026-10-15", "2026-10-19", "2026-10-26"],
        ["2026-10-30", "2026-11-03", "2026-11-10"],
        ["2026-11-13", "2026-11-17", "2026-11-24"],
        ["2026-11-30", "2026-12-02", "2026-12-09"],
        ["2026-12-15", "2026-12-17", "2026-12-24"],
        ["2026-12-31", "2027-01-05", "2027-01-12"],
    ];

    public static TheoryData<string, string, string> PublishedCalendar
    {
        get
        {
            var data = new TheoryData<string, string, string>();
            foreach (var row in PublishedRows)
                data.Add(row[0], row[1], row[2]);
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(PublishedCalendar))]
    public void ForSettlementDate_MatchesFinrasPublishedCalendar(
        string settlement,
        string due,
        string publication
    )
    {
        var cycle = ShortInterestCalendar.ForSettlementDate(DateOnly.Parse(settlement));

        cycle.DueDate.Should().Be(DateOnly.Parse(due));
        cycle.PublicationDate.Should().Be(DateOnly.Parse(publication));
    }

    [Fact]
    public void CyclesInYear_2026_YieldsFinrasTwentyFourSettlementDates()
    {
        // The settlement-date rule on its own: the 15th and the month end, each rolled back to
        // the prior trading day. A missing or extra cycle would slip past the per-row theory.
        var expected = PublishedRows
            .Select(row => DateOnly.Parse(row[0]))
            .Where(date => date.Year == 2026)
            .ToList();

        var settlementDates = ShortInterestCalendar
            .CyclesInYear(2026)
            .Select(cycle => cycle.SettlementDate)
            .ToList();

        settlementDates.Should().HaveCount(24);
        settlementDates.Should().Equal(expected);
    }

    [Fact]
    public void PublishingOn_APublicationDate_ReturnsThatCycle()
    {
        var cycle = ShortInterestCalendar.PublishingOn(new DateOnly(2026, 7, 24));

        cycle.Should().NotBeNull();
        cycle.SettlementDate.Should().Be(new DateOnly(2026, 7, 15));
    }

    [Fact]
    public void PublishingOn_ADateNothingPublishes_ReturnsNull()
    {
        ShortInterestCalendar.PublishingOn(new DateOnly(2026, 7, 23)).Should().BeNull();
    }

    [Fact]
    public void PublishingOn_EarlyJanuary_ResolvesThePreviousYearsDecemberSettlement()
    {
        // The December 31 cycle publishes in January, so the lookup must reach back a year.
        var cycle = ShortInterestCalendar.PublishingOn(new DateOnly(2026, 1, 12));

        cycle.Should().NotBeNull();
        cycle.SettlementDate.Should().Be(new DateOnly(2025, 12, 31));
    }

    [Fact]
    public void Upcoming_ReturnsTheNextCyclesOldestFirst()
    {
        var upcoming = ShortInterestCalendar.Upcoming(new DateOnly(2026, 7, 20), count: 2);

        upcoming
            .Select(cycle => cycle.SettlementDate)
            .Should()
            .Equal(new DateOnly(2026, 7, 15), new DateOnly(2026, 7, 31));
    }

    [Fact]
    public void Upcoming_OnAPublicationDate_StillListsThatDaysCycle()
    {
        // The file lands in the evening, so on its publication date the cycle is still pending.
        var upcoming = ShortInterestCalendar.Upcoming(new DateOnly(2026, 7, 24), count: 1);

        upcoming.Single().SettlementDate.Should().Be(new DateOnly(2026, 7, 15));
        upcoming.Single().PublicationDate.Should().Be(new DateOnly(2026, 7, 24));
    }

    [Fact]
    public void Upcoming_WithAStoredSettlementFloor_SkipsCyclesAlreadyCaptured()
    {
        // Once the July 15 file has landed it must drop off the "next reporting dates" list
        // even though its publication date is still today.
        var upcoming = ShortInterestCalendar.Upcoming(
            new DateOnly(2026, 7, 24),
            count: 2,
            afterSettlementDate: new DateOnly(2026, 7, 15)
        );

        upcoming
            .Select(cycle => cycle.SettlementDate)
            .Should()
            .Equal(new DateOnly(2026, 7, 31), new DateOnly(2026, 8, 14));
    }

    [Fact]
    public void Upcoming_AcrossTheYearBoundary_ContinuesIntoTheNextYear()
    {
        // The December 15 cycle published on the 24th, so only the December 31 cycle is still
        // pending in 2026 and the list has to roll into the next year to fill its count.
        var upcoming = ShortInterestCalendar.Upcoming(new DateOnly(2026, 12, 28), count: 3);

        upcoming
            .Select(cycle => cycle.SettlementDate)
            .Should()
            .Equal(
                new DateOnly(2026, 12, 31),
                new DateOnly(2027, 1, 15),
                new DateOnly(2027, 1, 29)
            );
    }

    [Fact]
    public void Upcoming_CountZero_ReturnsEmpty()
    {
        ShortInterestCalendar.Upcoming(new DateOnly(2026, 7, 24), count: 0).Should().BeEmpty();
    }
}
