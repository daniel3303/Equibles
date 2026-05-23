using Equibles.Holdings.HostedService;

namespace Equibles.UnitTests.Holdings;

/// <summary>
/// TryParseDatePart validates the day against DateTime.DaysInMonth, so
/// February 29 is accepted in leap years and rejected otherwise. A refactor
/// that hardcoded a max-day-per-month table with February = 28 would silently
/// reject every Q1-ending data set whose publication window spans a leap-year
/// boundary (e.g. "01dec2023-29feb2024_form13f.zip").
/// </summary>
public class Holdings13FRealtimeWorkerParseDataSetEndDateLeapYearFeb29Tests
{
    [Fact]
    public void ParseDataSetEndDate_LeapYearFeb29_ParsesSuccessfully()
    {
        var result = Holdings13FRealtimeWorker.ParseDataSetEndDate(
            "01dec2023-29feb2024_form13f.zip"
        );

        result.Should().Be(new DateOnly(2024, 2, 29));
    }
}
