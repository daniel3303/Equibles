using System.Reflection;
using Equibles.Errors.BusinessLogic;

namespace Equibles.UnitTests.Errors;

public class ErrorManagerTruncateNoSurrogateAtCutTests
{
    // Sibling to ErrorManagerTruncateSurrogatePairTests and TruncateNullTests.
    // The surrogate sibling pins the step-back arm (when char.IsHighSurrogate
    // is true at the cut point). The null sibling pins the null guard. Neither
    // pins the NORMAL truncation path — the common case where the cut point
    // does NOT split a surrogate pair and the result should be EXACTLY the
    // first maxLength characters.
    //
    // The Truncate ternary:
    //   var end = char.IsHighSurrogate(value[maxLength - 1]) ? maxLength - 1 : maxLength;
    // The `: maxLength` arm is what carries every ASCII / BMP truncation in
    // production (almost every error message and stack trace the worker
    // generates). A refactor that "added safety" by hard-coding `end =
    // maxLength - 1` to "always avoid the surrogate risk" would compile
    // cleanly, pass the surrogate sibling (still steps back), pass the null
    // sibling (still returns null), and silently shorten EVERY truncated
    // error message by one character — visible as off-by-one truncation in
    // the dashboard's recent-errors list, but invisible to the test suite.
    //
    // Pin: a pure-ASCII string longer than maxLength produces exactly the
    // first maxLength characters. The expected result's length is the
    // adversarial signal — `maxLength - 1` would fail this pin.
    [Fact]
    public void Truncate_ValueExceedsMaxLengthWithNoSurrogateAtCut_TakesExactlyMaxLengthCharacters()
    {
        var method = typeof(ErrorManager).GetMethod(
            "Truncate",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var result = (string)method.Invoke(null, ["abcdefghij", 7]);

        result.Should().Be("abcdefg");
        result.Length.Should().Be(7);
    }
}
