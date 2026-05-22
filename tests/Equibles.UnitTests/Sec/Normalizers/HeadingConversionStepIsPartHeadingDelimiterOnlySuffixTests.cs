using System.Reflection;
using Equibles.Sec.BusinessLogic.Normalizers;

namespace Equibles.UnitTests.Sec.Normalizers;

/// <summary>
/// IsPartHeading is a discriminator: given any string, it returns true when the
/// text matches the canonical SEC 10-K "PART [letter-word]" pattern and false
/// otherwise. Returning false is the entire purpose of the negative branch — a
/// throw is a contract violation, because every caller treats the bool as a
/// classification answer. The internal pipeline (HeadingConversionStep.Execute
/// → ClassifyHeadingTag → IsPartHeading) feeds in arbitrary span text harvested
/// from SEC EDGAR HTML, including separator-only fragments like "Part —" or
/// "Part -" that show up as visual section dividers. None of those callers
/// guard against the helper throwing, so an exception here bubbles up and
/// aborts the whole document normalization.
/// </summary>
public class HeadingConversionStepIsPartHeadingDelimiterOnlySuffixTests
{
    [Fact]
    public void IsPartHeading_PartFollowedByDelimiterOnlySuffix_ReturnsFalseWithoutThrowing()
    {
        var method = typeof(HeadingConversionStep).GetMethod(
            "IsPartHeading",
            BindingFlags.NonPublic | BindingFlags.Instance
        )!;
        var step = new HeadingConversionStep();

        // "Part -" passes every guard up to the Split: it starts with "PART", is
        // longer than 4 chars, has whitespace at index 4, and the substring after
        // position 5 trims to non-empty "-". A canonical roman-numeral suffix
        // ("I", "II", "IV") splits into a letter-only first word; an
        // all-delimiter suffix ("-") splits into nothing under
        // StringSplitOptions.RemoveEmptyEntries. The method should answer "no,
        // not a part heading" — not throw.
        bool Invoke()
        {
            try
            {
                return (bool)method.Invoke(step, ["Part -"])!;
            }
            catch (TargetInvocationException ex)
            {
                throw ex.InnerException!;
            }
        }

        var act = Invoke;

        act.Should()
            .NotThrow(
                "IsPartHeading is a classifier — every input must return true or false, never throw"
            );
        Invoke().Should().BeFalse("'-' is not a canonical SEC 10-K roman-numeral part suffix");
    }
}
