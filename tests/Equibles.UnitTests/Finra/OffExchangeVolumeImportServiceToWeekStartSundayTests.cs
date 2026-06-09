using System.Reflection;
using Equibles.Finra.HostedService.Services;

namespace Equibles.UnitTests.Finra;

public class OffExchangeVolumeImportServiceToWeekStartSundayTests
{
    // Contract: ToWeekStart normalizes any date to "the Monday that starts its FINRA
    // reporting week". Sunday is the adversarial case — DayOfWeek is 0, so the
    // ((int)DayOfWeek + 6) % 7 offset must wrap to 6, and it is where US-week
    // (Sunday-start) and ISO-week (Monday-start) conventions diverge. A Sunday must map
    // back to the PREVIOUS Monday (its week's start), not forward to the next one.
    [Fact]
    public void ToWeekStart_Sunday_ReturnsPreviousMonday()
    {
        var method = typeof(OffExchangeVolumeImportService).GetMethod(
            "ToWeekStart",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        // 2024-03-10 is a Sunday; the Monday that starts its week is 2024-03-04.
        var result = (DateOnly)method!.Invoke(null, [new DateOnly(2024, 3, 10)]);

        result.Should().Be(new DateOnly(2024, 3, 4));
        result.DayOfWeek.Should().Be(DayOfWeek.Monday);
    }
}
