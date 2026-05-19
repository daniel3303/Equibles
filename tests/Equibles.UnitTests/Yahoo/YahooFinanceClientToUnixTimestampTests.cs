using System.Reflection;
using Equibles.Integrations.Yahoo;

namespace Equibles.UnitTests.Yahoo;

public class YahooFinanceClientToUnixTimestampTests
{
    private static readonly MethodInfo ToUnix = typeof(YahooFinanceClient).GetMethod(
        "ToUnixTimestamp",
        BindingFlags.NonPublic | BindingFlags.Static
    )!;
    private static readonly MethodInfo FromUnix = typeof(YahooFinanceClient).GetMethod(
        "FromUnixTimestamp",
        BindingFlags.NonPublic | BindingFlags.Static
    )!;

    // Contract: ToUnixTimestamp anchors a DateOnly at UTC midnight relative to
    // 1970-01-01Z, so 1970-01-01 must be exactly 0 and the conversion must
    // round-trip (price-range building converts dates → timestamps → dates).
    // A local-time or off-by-one anchor would break either invariant.
    [Fact]
    public void ToUnixTimestamp_EpochIsZeroAndRoundTripsViaFromUnixTimestamp()
    {
        ((long)ToUnix.Invoke(null, [new DateOnly(1970, 1, 1)])!).Should().Be(0L);

        var date = new DateOnly(2021, 7, 19);
        var ts = (long)ToUnix.Invoke(null, [date])!;
        ((DateOnly)FromUnix.Invoke(null, [ts])!).Should().Be(date);
    }
}
