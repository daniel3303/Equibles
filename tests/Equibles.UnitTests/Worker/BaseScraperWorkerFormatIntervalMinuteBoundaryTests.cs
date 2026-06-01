using System.Reflection;
using Equibles.Worker;

namespace Equibles.UnitTests.Worker;

public class BaseScraperWorkerFormatIntervalMinuteBoundaryTests
{
    [Fact]
    public void FormatInterval_ExactlyOneMinute_RendersInMinutesUnit()
    {
        // Contract: the three-arm cascade picks the largest unit that is >= 1, so
        // exactly 60 seconds is one full minute and renders "1m", not "60s". The
        // existing mid-range pins (5m, 30s) miss this seconds/minutes boundary — a
        // regression flipping the minutes guard from >= to > would drop 60s to "60s".
        var method = typeof(BaseScraperWorker).GetMethod(
            "FormatInterval",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var result = (string)method!.Invoke(null, [TimeSpan.FromSeconds(60)]);

        result.Should().Be("1m");
    }
}
