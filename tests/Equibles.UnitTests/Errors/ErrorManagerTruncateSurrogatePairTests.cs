using System.Reflection;
using Equibles.Errors.BusinessLogic;

namespace Equibles.UnitTests.Errors;

// Lane A (adversarial): Truncate's contract says "caps a value to maxLength
// UTF-16 units WITHOUT splitting a surrogate pair." A surrogate pair that
// straddles the cut point (high surrogate at maxLength-1) must step back by
// one, producing a shorter-than-maxLength result rather than a dangling lone
// surrogate that corrupts the text column and JSON serialization.
public class ErrorManagerTruncateSurrogatePairTests
{
    private static readonly MethodInfo TruncateMethod = typeof(ErrorManager).GetMethod(
        "Truncate",
        BindingFlags.NonPublic | BindingFlags.Static
    );

    [Fact]
    public void Truncate_CutPointSplitsSurrogatePair_StepsBackToAvoidDanglingSurrogate()
    {
        // U+1F4A5 (COLLISION SYMBOL) is a surrogate pair: 💥 (2 UTF-16 units).
        // Build a string of 9 ASCII chars + the emoji = 11 UTF-16 units.
        // Truncate to 10: maxLength-1 = index 9 = \uD83D (high surrogate).
        // Contract: step back to 9, yielding only the 9 ASCII chars.
        var input = "123456789\U0001F4A5";
        input.Length.Should().Be(11);

        var result = (string)TruncateMethod.Invoke(null, [input, 10]);

        result.Should().Be("123456789");
        result.Length.Should().Be(9);
    }
}
