using System.Reflection;
using Equibles.Sec.HostedService.Services;

namespace Equibles.UnitTests.Sec;

public class InsiderTradingFilingProcessorParseLongDirectSuccessTests
{
    // ParseLong is the share-count / volume / vote-authority parser for
    // every Form 4 row that reaches the database. Its body is a TIERED
    // fallback chain:
    //   1. IsNullOrEmpty → 0  (UNPINNED — null/empty wire is common)
    //   2. long.TryParse success → return result  (PINNED HERE — the happy path)
    //   3. ParseDecimal then range-check truncate (pinned by Overflow,
    //      NegativeOverflow, and DecimalString siblings)
    //
    // The existing three pins all exercise arm 3 — they use decimal-formatted
    // inputs that long.TryParse REJECTS, forcing the fallback. None pins the
    // dominant production path: integer-shaped inputs that long.TryParse
    // ACCEPTS verbatim. The "12345" → 12345 result is the assumed-but-untested
    // backbone of every Form 4 share count.
    //
    // The risk this pin uniquely catches:
    //   • Drop-the-long.TryParse-arm — `value` falls straight through to
    //     ParseDecimal — would compile, every integer input would still
    //     work via the decimal path (decimal.TryParse accepts "12345"
    //     just fine), but the contract becomes "decimal semantics"
    //     instead of "long semantics". The downstream effect: inputs
    //     long.TryParse REJECTS but ParseDecimal accepts (like "1,000"
    //     under NumberStyles.Any/Invariant — comma is thousands separator)
    //     would now succeed. Real Form 4 wire strings don't contain
    //     thousand separators, but a future SEC schema variant that
    //     adds them would silently start parsing where the contract
    //     said it should fail.
    //   • Swap-to-ParseDecimal-first refactor — would compile and pass
    //     every existing sibling (all use decimal-shaped inputs that
    //     succeed either way), and pass THIS pin (12345 succeeds in
    //     decimal too). Benign for behavior. The diagnostic value of
    //     this pin is the documented intent: long-first, decimal-
    //     fallback.
    //   • Swap-to-TruncatedToInt regression — `(long)int.Parse(value)`
    //     would compile, fail on inputs above int.MaxValue (~2.1B);
    //     real Form 4 share counts on large institutions exceed int.
    //     Pin uses a >int.MaxValue input to catch this.
    //
    // Pin: invoke with 3_000_000_000L (just above int.MaxValue = ~2.1B)
    // and assert the returned long is exactly that value. This:
    //   • Exercises ONLY arm 2 (long.TryParse succeeds directly).
    //   • Distinguishes the working long parser from any swap to an
    //     int-based parser.
    //   • Doesn't conflict with the overflow/negative-overflow pins
    //     (those use decimal-formatted inputs above long.MaxValue,
    //     ~9.2 × 10¹⁸).
    //
    // Reflection-invoke since internal static.
    [Fact]
    public void ParseLong_IntegerShapedInputAboveIntMaxValue_ParsesDirectlyViaLongTryParseArm()
    {
        var method = typeof(InsiderTradingFilingProcessor).GetMethod(
            "ParseLong",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var result = (long)method!.Invoke(null, ["3000000000"]);

        result.Should().Be(3_000_000_000L);
    }
}
