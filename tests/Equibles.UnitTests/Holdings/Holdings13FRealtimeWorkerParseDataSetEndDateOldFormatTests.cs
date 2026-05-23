using Equibles.Holdings.HostedService;

namespace Equibles.UnitTests.Holdings;

/// <summary>
/// Pins the old-format parsing path ("2023q4_form13f.zip") which computes
/// the last day of the quarter — previously only exercised by invalid-input
/// tests; no happy-path coverage existed.
/// </summary>
public class Holdings13FRealtimeWorkerParseDataSetEndDateOldFormatTests
{
    [Fact]
    public void ParseDataSetEndDate_OldFormatQ4_ReturnsDecember31()
    {
        var result = Holdings13FRealtimeWorker.ParseDataSetEndDate("2023q4_form13f.zip");

        result.Should().Be(new DateOnly(2023, 12, 31));
    }
}
