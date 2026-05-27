using System.Reflection;
using Equibles.Worker;

namespace Equibles.UnitTests.Worker;

public class BaseScraperWorkerFormatIntervalSecondsTests
{
    [Fact]
    public void FormatInterval_SubMinuteInterval_RendersInSecondsUnit()
    {
        // Sibling to the existing Minutes pin. FormatInterval's three-arm cascade
        // (BaseScraperWorker.cs:179-183) picks the largest non-zero unit: hours
        // ≥ 1, else minutes ≥ 1, else seconds. The seconds arm is the only one
        // that uses N0 formatting (whole number) — a copy-paste swap of the
        // minutes (0.#) body into the seconds path would render a 30-second
        // sleep as "30s" still, but a 1.5-second retry as "1.5s" instead of
        // "2s" (or, the inverse swap, render 30-second as "30.0s"). Pin the
        // canonical "N0 — whole seconds" output so format-string drift is
        // caught by this test, not by an operator confused at log noise.
        var method = typeof(BaseScraperWorker).GetMethod(
            "FormatInterval",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var result = (string)method!.Invoke(null, [TimeSpan.FromSeconds(30)]);

        result.Should().Be("30s");
    }
}
