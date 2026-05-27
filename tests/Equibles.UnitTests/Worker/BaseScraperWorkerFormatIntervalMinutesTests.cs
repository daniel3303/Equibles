using System.Reflection;
using Equibles.Worker;

namespace Equibles.UnitTests.Worker;

public class BaseScraperWorkerFormatIntervalMinutesTests
{
    // FormatInterval picks the most relevant unit by descending order: hours
    // first, then minutes, then seconds. The minutes arm is the middle range
    // (1 min ≤ interval < 1 hour) — the band every operator sees in the
    // worker's "next run in Nm" startup log. A copy-paste swap of the
    // minutes and seconds bodies would silently render a 5-minute sleep as
    // "300s" — operationally usable but a regression from the deliberate
    // unit picker. Pin the minutes branch on a value plainly inside the
    // band so any reorder of the return statements is loud.
    [Fact]
    public void FormatInterval_FiveMinuteInterval_RendersInMinutesUnit()
    {
        var method = typeof(BaseScraperWorker).GetMethod(
            "FormatInterval",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var result = (string)method.Invoke(null, [TimeSpan.FromMinutes(5)]);

        result.Should().Be("5m");
    }
}
