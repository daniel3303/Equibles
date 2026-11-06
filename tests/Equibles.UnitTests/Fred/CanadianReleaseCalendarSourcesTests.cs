using Equibles.Fred.Data.Models;
using Equibles.Fred.HostedService.Canada;

namespace Equibles.UnitTests.Fred;

// Statistics Canada's schedule and the Bank of Canada's key-rate page as served on 2026-11-06.
public class CanadianReleaseCalendarSourcesTests
{
    private static readonly DateOnly Today = new(2026, 11, 6);

    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fred", "Fixtures", name));

    [Fact]
    public void StatCan_KeepsUpcomingRowsWithTheirReferencePeriods()
    {
        var dates = CanadianReleaseCalendarSources.ParseStatCan(
            Fixture("statcan-schedule-2026-11-06.json"),
            Today
        );

        dates.Should().OnlyContain(date => date.Date >= Today);
        dates
            .Should()
            .Contain(
                new CanadianReleaseDate(
                    "Consumer Price Index",
                    new DateOnly(2026, 11, 16),
                    "October 2026"
                )
            );
        dates
            .Should()
            .Contain(
                new CanadianReleaseDate(
                    "Labour Force Survey",
                    new DateOnly(2026, 12, 4),
                    "November 2026"
                )
            );
    }

    [Fact]
    public void StatCan_AScheduleThatEndedIsRefusedNotReadAsNoReleases()
    {
        var read = () =>
            CanadianReleaseCalendarSources.ParseStatCan(
                Fixture("statcan-schedule-2026-11-06.json"),
                new DateOnly(2030, 1, 1)
            );

        read.Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("<html></html>")]
    [InlineData("[{\"title\":\"Consumer Price Index\",\"description\":\"October 2026\"}]")]
    [InlineData("[{\"date\":\"16 November\",\"title\":\"Consumer Price Index\"}]")]
    public void StatCan_AnotherShapeIsRefused(string body)
    {
        var read = () => CanadianReleaseCalendarSources.ParseStatCan(body, Today);

        read.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void BankOfCanada_ReadsEachDecisionAndTheReportsThatComeWithIt()
    {
        var dates = CanadianReleaseCalendarSources.ParseBankOfCanada(
            Fixture("boc-key-interest-rate-2026-11-06.html"),
            Today
        );

        var decisions = dates
            .Where(date => date.Name == CanadianReleaseCalendarSources.InterestRateAnnouncement)
            .Select(date => date.Date)
            .ToList();
        var reports = dates
            .Where(date => date.Name == CanadianReleaseCalendarSources.MonetaryPolicyReport)
            .Select(date => date.Date)
            .ToList();
        decisions
            .Should()
            .Equal(
                new DateOnly(2026, 12, 9),
                new DateOnly(2027, 1, 27),
                new DateOnly(2027, 3, 3),
                new DateOnly(2027, 4, 28),
                new DateOnly(2027, 6, 2),
                new DateOnly(2027, 7, 21),
                new DateOnly(2027, 9, 8),
                new DateOnly(2027, 10, 27),
                new DateOnly(2027, 12, 8)
            );
        reports
            .Should()
            .Equal(
                new DateOnly(2027, 1, 27),
                new DateOnly(2027, 4, 28),
                new DateOnly(2027, 7, 21),
                new DateOnly(2027, 10, 27)
            );
    }

    [Theory]
    [InlineData("<html><body>Page moved</body></html>")]
    [InlineData(
        "<table><caption>Schedule for 2027</caption><tbody><tr><td>January 27</td><td>Interest rate announcement</td></tr></tbody></table>"
    )]
    public void BankOfCanada_AnotherShapeIsRefused(string body)
    {
        var read = () => CanadianReleaseCalendarSources.ParseBankOfCanada(body, Today);

        read.Should().Throw<InvalidDataException>();
    }

    // The import replaces stored dates with this reading, so a page that changed under one row or one table is refused,
    // never read without them.
    [Theory]
    [InlineData("<td>December", "<td class=\"next\">December")]
    [InlineData("Schedule for 2027", "Dates for 2027")]
    public void BankOfCanada_AChangedRowOrTableRefusesThePage(string original, string changed)
    {
        var read = () =>
            CanadianReleaseCalendarSources.ParseBankOfCanada(
                Fixture("boc-key-interest-rate-2026-11-06.html").Replace(original, changed),
                Today
            );

        read.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void BankOfCanada_ATableBodyAttributeKeepsEachYearInItsOwnTable()
    {
        var html = Fixture("boc-key-interest-rate-2026-11-06.html");

        var dates = CanadianReleaseCalendarSources.ParseBankOfCanada(
            html.Replace("<tbody>", "<tbody class=\"schedule\">"),
            Today
        );

        dates.Should().Equal(CanadianReleaseCalendarSources.ParseBankOfCanada(html, Today));
    }

    [Theory]
    [InlineData("Consumer Price Index", FredReleaseImportance.High)]
    [InlineData("Interest rate announcement", FredReleaseImportance.High)]
    [InlineData("Retail trade", FredReleaseImportance.Medium)]
    [InlineData("National tourism indicators", FredReleaseImportance.Low)]
    public void ReleasesTakeTheirCuratedTier(string name, FredReleaseImportance expected)
    {
        CanadianReleaseCalendarSources.Importance(name).Should().Be(expected);
    }
}
