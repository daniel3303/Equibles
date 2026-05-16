using System.Reflection;
using Equibles.Integrations.Yahoo;

namespace Equibles.UnitTests.Yahoo;

public class YahooFinanceClientUtcDateTests
{
    private static readonly MethodInfo FromUnixTimestampMethod =
        typeof(YahooFinanceClient).GetMethod(
            "FromUnixTimestamp",
            BindingFlags.NonPublic | BindingFlags.Static
        );

    // Contract: FromUnixTimestamp returns the UTC calendar date of the instant
    // (anchored on UnixEpoch ... .UtcDateTime). 1704067199 = 2023-12-31T23:59:59Z,
    // whose UTC date is 2023-12-31 but whose LOCAL date is 2024-01-01 on any
    // UTC+ machine. The existing pin uses exact-midnight 1704067200 where UTC
    // and local dates coincide, so it cannot catch a local-time regression;
    // this instant can. Yahoo daily candles are not midnight-aligned.
    [Fact]
    public void FromUnixTimestamp_OneSecondBeforeMidnightUtc_ReturnsUtcCalendarDateNotLocal()
    {
        var result = (DateOnly)FromUnixTimestampMethod.Invoke(null, [1704067199L]);

        result.Should().Be(new DateOnly(2023, 12, 31));
    }
}
