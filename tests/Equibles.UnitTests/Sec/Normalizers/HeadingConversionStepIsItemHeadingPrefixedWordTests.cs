using System.Reflection;
using Equibles.Sec.BusinessLogic.Normalizers;

namespace Equibles.UnitTests.Sec.Normalizers;

public class HeadingConversionStepIsItemHeadingPrefixedWordTests
{
    // Sibling pin to HeadingConversionStepIsPartHeadingTests, which pins
    // both the canonical "Part IV" path AND the prefix-word false-positive
    // ("Participants" → false). IsItemHeading's only existing pin is the
    // NBSP separator case — the parallel prefix-word false-positive
    // ("Itemize" / "Itemized" / "Items") is unpinned.
    //
    // The whitespace check at upperText[4] is what guards against
    // false-positives: any word that *starts* with "Item" but is a longer
    // word (Itemize, Itemized, Itemization, Items) has a letter at
    // position 4 instead of whitespace. SEC 10-K body text occasionally
    // contains sentences like "Itemize the risks below" or "Items 1 and
    // 2 are mandatory" — these must NOT be promoted to H2 item-headings
    // in the chunker.
    //
    // The risks this pin uniquely catches and the NBSP sibling cannot:
    //   • Drop the `char.IsWhiteSpace(upperText[4])` guard. The NBSP
    //     sibling passes because it tests an INPUT where [4] is
    //     whitespace; it cannot fail when the guard is dropped (the
    //     dropped guard still admits whitespace-at-4 inputs). The
    //     prefix-word case is the inverse — [4] is a LETTER, and
    //     dropping the guard would silently promote every "Itemize"
    //     sentence to H2.
    //   • Swap to `char.IsLetterOrDigit(upperText[4])` (inverted
    //     intent) — same observable promotion.
    //
    // Pin: "Itemize" (canonical longer word starting with "Item")
    // must classify as NOT an item heading.
    [Fact]
    public void IsItemHeading_ItemPrefixedWord_ReturnsFalse()
    {
        var method = typeof(HeadingConversionStep).GetMethod(
            "IsItemHeading",
            BindingFlags.NonPublic | BindingFlags.Instance
        );
        var step = new HeadingConversionStep();

        var result = (bool)method.Invoke(step, ["Itemize"]);

        result.Should().BeFalse();
    }
}
