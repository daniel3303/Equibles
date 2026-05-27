using System.Reflection;
using Equibles.Worker;

namespace Equibles.UnitTests.Worker;

public class BaseScraperWorkerFormatIntervalHoursTests
{
    [Fact]
    public void FormatInterval_MultiHourInterval_RendersInHoursUnit()
    {
        // Closes the three-arm family (Minutes + Seconds pinned earlier).
        // FormatInterval's hours arm fires when `interval.TotalHours >= 1`
        // (BaseScraperWorker.cs:179). The hours format uses `0.#` so a 2-hour
        // sleep renders as "2h" and a 1.5-hour sleep as "1.5h". A refactor
        // that drops the descending unit picker (e.g. always using minutes)
        // would silently render a 6-hour worker interval as "360m" — still
        // operationally usable but a regression from the deliberate
        // operator-facing unit, hiding hours-scale cycles behind a minutes
        // wall. Pin a clean multi-hour value to lock the unit + format.
        var method = typeof(BaseScraperWorker).GetMethod(
            "FormatInterval",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var result = (string)method!.Invoke(null, [TimeSpan.FromHours(2)]);

        result.Should().Be("2h");
    }
}
