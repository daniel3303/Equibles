using Equibles.Holdings.HostedService;

namespace Equibles.UnitTests.Holdings;

/// <summary>
/// Pins the new-format parsing path ("01dec2025-28feb2026_form13f.zip") which
/// exercises TryParseDatePart end-to-end — currently only exercised by
/// invalid-input tests; no happy-path coverage existed.
/// </summary>
public class Holdings13FRealtimeWorkerParseDataSetEndDateNewFormatTests
{
    [Fact]
    public void ParseDataSetEndDate_NewFormat_ReturnsEndDate()
    {
        var result = Holdings13FRealtimeWorker.ParseDataSetEndDate(
            "01dec2025-28feb2026_form13f.zip"
        );

        result.Should().Be(new DateOnly(2026, 2, 28));
    }
}
