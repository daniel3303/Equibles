using System.Reflection;
using Equibles.Yahoo.Data.Prices;
using Equibles.Yahoo.HostedService.Services;

namespace Equibles.UnitTests.Yahoo;

/// <summary>
/// The import service keeps private static forwarders with the guards' original names so the
/// reflection-based tests keep resolving; this pins that each forwarder still answers exactly
/// as the shared rule it delegates to.
/// </summary>
public class YahooGuardForwarderTests
{
    private static MethodInfo Resolve(string name) =>
        typeof(YahooPriceImportService).GetMethod(
            name,
            BindingFlags.NonPublic | BindingFlags.Static
        );

    [Theory]
    [InlineData(100, 100.5)]
    [InlineData(100, 80)]
    [InlineData(0, 100)]
    public void IsSameSplitBasis_ForwardsToTheSharedRule(decimal stored, decimal fetched)
    {
        var forwarded = (bool)Resolve("IsSameSplitBasis").Invoke(null, [stored, fetched]);
        forwarded.Should().Be(DailyBarGuards.IsSameSplitBasis(stored, fetched));
    }

    [Theory]
    [InlineData(1_000L, 1_001L)]
    [InlineData(1_000L, 1_000L)]
    public void IsVolumeUpgrade_ForwardsToTheSharedRule(long stored, long fetched)
    {
        var forwarded = (bool)Resolve("IsVolumeUpgrade").Invoke(null, [stored, fetched]);
        forwarded.Should().Be(DailyBarGuards.IsVolumeUpgrade(stored, fetched));
    }

    [Fact]
    public void IsSettledDailyBar_ForwardsToTheSharedRule()
    {
        var today = new DateOnly(2026, 9, 16);
        foreach (var offset in new[] { -1, 0, 1 })
        {
            var bar = today.AddDays(offset);
            var forwarded = (bool)Resolve("IsSettledDailyBar").Invoke(null, [bar, today]);
            forwarded.Should().Be(DailyBarGuards.IsSettledDailyBar(bar, today));
        }
    }
}
