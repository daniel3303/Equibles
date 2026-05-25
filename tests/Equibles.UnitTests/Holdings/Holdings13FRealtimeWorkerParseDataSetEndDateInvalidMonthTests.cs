using Equibles.Holdings.HostedService;

namespace Equibles.UnitTests.Holdings;

public class Holdings13FRealtimeWorkerParseDataSetEndDateInvalidMonthTests
{
    [Fact]
    public void ParseDataSetEndDate_NewFormatUnrecognizedMonth_ReturnsNull()
    {
        // Contract: the new-format parser expects a 3-letter month abbreviation
        // (jan–dec). An unrecognized abbreviation must yield null, not throw.
        var result = Holdings13FRealtimeWorker.ParseDataSetEndDate(
            "01dec2025-28xyz2026_form13f.zip"
        );

        result.Should().BeNull();
    }
}
