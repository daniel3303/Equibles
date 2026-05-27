using System.Reflection;
using Equibles.Sec.HostedService.Services;

namespace Equibles.UnitTests.Sec;

public class InsiderTradingFilingProcessorParseDecimalNullInputTests
{
    // Sibling to ParseDecimal_ValueWithThousandsSeparator_ParsesCorrectlyViaNumberStylesAnyAndInvariantCulture
    // (existing). That pin covers the SUCCESS arm of the TryParse ternary on
    // a well-formed thousand-separated input. This pin covers the structurally
    // distinct null-input GUARD arm at the top of the helper:
    //   if (string.IsNullOrEmpty(value)) return 0;
    //
    // ParseDecimal is the final-step parser for Form 4 quantitative wire
    // fields (transactionPricePerShare, transactionShares-when-fractional,
    // sharesOwnedFollowingTransaction-decimal-arms, etc.). Real SEC
    // ownership XML routinely omits these elements — restricted-stock-unit
    // grants have no price, gift transactions have no per-share dollar
    // amount, post-conversion reports may strip the post-balance — so
    // `value == null` is a regular production input via the
    // `GetWrappedValue(...)?.Trim()` chain that feeds ParseDecimal.
    //
    // The risk this pin uniquely catches:
    //   • Drop-the-IsNullOrEmpty guard — `decimal.TryParse(null, ...)`
    //     doesn't throw in .NET (returns false), so the helper would
    //     return 0 anyway via the false-arm of the ternary. The guard
    //     drop is therefore behavior-preserving in isolation. But a
    //     refactor that swapped `decimal.TryParse` for a different
    //     parser (e.g. `decimal.Parse(value, ...)`) AND dropped the
    //     guard would NRE on null input. Pinning the guard explicitly
    //     defends against this two-step refactor.
    //   • Drop-the-fallback-zero — `: throw new FormatException(...)` —
    //     would compile, pass the existing thousands-separator sibling
    //     (it returns via the success arm), and crash every Form 4
    //     filing with a missing decimal field. The platform would lose
    //     every restricted-stock-unit grant from the insider-transactions
    //     feed silently (each filing aborts at the first null field).
    //   • Swap-to-MaxValue or -MinValue regression — would propagate
    //     sentinel values into the database. Caught by the exact-zero
    //     assertion.
    //
    // Pin: invoke with null and assert the result is exactly 0m. The
    // null input is the most adversarial — both the IsNullOrEmpty guard
    // AND any downstream TryParse(null, ...) call would each return 0
    // independently, so the assertion confirms the contract regardless
    // of which arm fires.
    //
    // Reflection-invoke since ParseDecimal is internal static.
    [Fact]
    public void ParseDecimal_NullInput_ReturnsZeroWithoutThrowing()
    {
        var method = typeof(InsiderTradingFilingProcessor).GetMethod(
            "ParseDecimal",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var act = () => (decimal)method!.Invoke(null, new object[] { null });

        var result = act.Should().NotThrow().Subject;
        result.Should().Be(0m);
    }
}
