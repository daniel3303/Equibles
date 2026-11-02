using Equibles.Integrations.Sec;
using Equibles.Integrations.Sec.Models;
using FluentAssertions;
using Xunit;

namespace Equibles.UnitTests.Sec;

public class SecRegistrantFilingsSuccessorRegistrationTests
{
    private static FilingData Filing(string form, DateOnly filed, string accession) =>
        new()
        {
            Form = form,
            FilingDate = filed,
            AccessionNumber = accession,
        };

    [Fact]
    public void NewestSuccessorReport_WinsOverOlderReportsAndOtherForms()
    {
        var filings = new[]
        {
            Filing("10-Q", new DateOnly(2026, 8, 7), "0001193125-26-340245"),
            Filing("8-K12G3", new DateOnly(2020, 3, 2), "0001193125-20-000001"),
            Filing("8-K12B", new DateOnly(2026, 10, 29), "0001193125-26-436899"),
            Filing("8-K12B/A", new DateOnly(2026, 10, 29), "0001193125-26-436900"),
            Filing("8-K", new DateOnly(2026, 10, 30), "0001193125-26-437000"),
        };

        SecRegistrantFilings
            .LatestSuccessorRegistration(filings)
            .Should()
            .Be(
                new SuccessorRegistration(
                    "8-K12B/A",
                    new DateOnly(2026, 10, 29),
                    "0001193125-26-436900"
                )
            );
    }

    [Fact]
    public void NoSuccessorReport_IsNull()
    {
        var filings = new[]
        {
            Filing("10-K", new DateOnly(2026, 2, 26), "0001193125-26-076753"),
            Filing("15-12G", new DateOnly(2026, 10, 29), "0001193125-26-437020"),
            Filing("8-K", new DateOnly(2026, 10, 30), "0001193125-26-437000"),
        };

        SecRegistrantFilings.LatestSuccessorRegistration(filings).Should().BeNull();
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(180, true)]
    [InlineData(181, false)]
    public void AReportIsCurrentWithinTheWindow(int daysAgo, bool expected)
    {
        var today = new DateOnly(2026, 11, 2);
        var registration = new SuccessorRegistration(
            "8-K12B",
            today.AddDays(-daysAgo),
            "0001193125-26-436899"
        );

        SecRegistrantFilings
            .IsCurrentSuccessorRegistration(registration, today)
            .Should()
            .Be(expected);
    }

    [Fact]
    public void NoReport_IsNeverCurrent()
    {
        SecRegistrantFilings
            .IsCurrentSuccessorRegistration(null, new DateOnly(2026, 11, 2))
            .Should()
            .BeFalse();
    }
}
