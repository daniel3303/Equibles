using System;
using System.Collections.Generic;
using System.IO;
using Equibles.FdaCatalysts.BusinessLogic;
using Equibles.FdaCatalysts.Data.Models;
using FluentAssertions;
using Xunit;

namespace Equibles.UnitTests.FdaCatalysts;

public class FdaAdvisoryCommitteeCalendarParserTests
{
    private static string FixturePath(string fileName) =>
        Path.Combine(AppContext.BaseDirectory, "TestAssets", "FdaCatalysts", fileName);

    private static IReadOnlyList<FdaCatalyst> ParseFixture() =>
        FdaAdvisoryCommitteeCalendarParser.Parse(
            File.ReadAllText(FixturePath("fda-advisory-committee-calendar.html"))
        );

    [Fact]
    public void Parse_RealCalendar_KeepsOnlyRowsWithAnAuthoritativeStartDate()
    {
        var catalysts = ParseFixture();

        // Five rows in the fixture; the stale 2016 row has an empty Start Date and is dropped.
        catalysts.Should().HaveCount(4);
        catalysts.Should().OnlyContain(c => c.CatalystType == FdaCatalystType.AdvisoryCommittee);
        catalysts
            .Should()
            .NotContain(c =>
                c.SourceReference
                == "november-9-10-2016-microbiology-devices-panel-medical-devices-advisory-committee-meeting"
            );
    }

    [Fact]
    public void Parse_MultiDayMeeting_MapsEveryColumnToTheEntity()
    {
        var pharmacy = ParseFixture()
            .Should()
            .ContainSingle(c => c.Title.Contains("Pharmacy Compounding"))
            .Subject;

        pharmacy.MeetingDate.Should().Be(new DateOnly(2026, 7, 23));
        pharmacy.EndDate.Should().Be(new DateOnly(2026, 7, 24));
        pharmacy.Center.Should().Be("Center for Drug Evaluation and Research");
        pharmacy
            .SourceReference.Should()
            .Be("july-23-24-2026-meeting-pharmacy-compounding-advisory-committee-07232026");
        pharmacy
            .SourceUrl.Should()
            .Be(
                "https://www.fda.gov/advisory-committees/advisory-committee-calendar/july-23-24-2026-meeting-pharmacy-compounding-advisory-committee-07232026"
            );
    }

    [Fact]
    public void Parse_SingleDayMeeting_SetsEndDateEqualToStart()
    {
        var tobacco = ParseFixture()
            .Should()
            .ContainSingle(c => c.Center == "Center for Tobacco Products")
            .Subject;

        tobacco.MeetingDate.Should().Be(new DateOnly(2026, 1, 22));
        tobacco.EndDate.Should().Be(new DateOnly(2026, 1, 22));
    }

    [Fact]
    public void Parse_TakesSlugFromTheMeetingAnchor()
    {
        var catalysts = ParseFixture();

        catalysts
            .Should()
            .Contain(c =>
                c.SourceReference
                    == "vaccines-and-related-biological-products-advisory-committee-june-18-2026-meeting-announcement"
                && c.MeetingDate == new DateOnly(2026, 6, 18)
            );
    }

    [Fact]
    public void Parse_EmptyOrTablelessHtml_ReturnsEmpty()
    {
        FdaAdvisoryCommitteeCalendarParser.Parse("").Should().BeEmpty();
        FdaAdvisoryCommitteeCalendarParser
            .Parse("<html><body><p>no table here</p></body></html>")
            .Should()
            .BeEmpty();
    }
}
