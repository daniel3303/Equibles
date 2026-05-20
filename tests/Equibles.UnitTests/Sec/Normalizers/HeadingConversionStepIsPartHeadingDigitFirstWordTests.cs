using System.Reflection;
using Equibles.Sec.BusinessLogic.Normalizers;

namespace Equibles.UnitTests.Sec.Normalizers;

/// <summary>
/// IsPartHeading is asymmetric with its sibling IsItemHeading: SEC 10-K Parts
/// are conventionally Roman numerals ("Part I" through "Part IV"), while Items
/// are Arabic numerals ("Item 1" through "Item 7"). IsPartHeading enforces the
/// Roman-only convention via a final `firstWord.All(char.IsLetter)` guard that
/// IsItemHeading deliberately omits. Existing tests cover canonical "Part IV",
/// the NBSP separator, and a "Participants" prefix-word false-positive — but
/// none pins the digit-rejection branch. A refactor that "harmonizes" the two
/// helpers (drops the letter-only guard under the intuition that "Part 1 looks
/// like a heading too") would compile, pass every existing pin, and silently
/// promote non-canonical digit-Part text to H2 in the chunker, polluting the
/// table-of-contents extraction.
/// </summary>
public class HeadingConversionStepIsPartHeadingDigitFirstWordTests
{
    [Fact]
    public void IsPartHeading_DigitAfterPart_ReturnsFalse()
    {
        var method = typeof(HeadingConversionStep).GetMethod(
            "IsPartHeading",
            BindingFlags.NonPublic | BindingFlags.Instance
        );
        var step = new HeadingConversionStep();

        var result = (bool)method.Invoke(step, ["Part 1"]);

        result.Should().BeFalse();
    }
}
