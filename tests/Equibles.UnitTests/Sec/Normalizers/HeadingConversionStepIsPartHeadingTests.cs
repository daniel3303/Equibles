using System.Reflection;
using Equibles.Sec.BusinessLogic.Normalizers;

namespace Equibles.UnitTests.Sec.Normalizers;

/// <summary>
/// IsPartHeading classifies SEC 10-K "PART" section headings. The Execute-level
/// test only covers the positive "PART I" path and is confounded by the
/// all-uppercase rule. This isolates the discriminator: a canonical part
/// heading is recognised, but a word merely prefixed "Part" (Participants,
/// Partnership…) must NOT be — the trailing space in StartsWith("PART ") is the
/// only thing preventing every such word from being mis-tagged as a heading.
/// </summary>
public class HeadingConversionStepIsPartHeadingTests
{
    [Fact]
    public void IsPartHeading_AcceptsCanonicalPartButRejectsPartPrefixedWord()
    {
        var method = typeof(HeadingConversionStep).GetMethod(
            "IsPartHeading",
            BindingFlags.NonPublic | BindingFlags.Instance
        );
        var step = new HeadingConversionStep();

        var canonical = (bool)method.Invoke(step, ["Part IV"]);
        var prefixedWord = (bool)method.Invoke(step, ["Participants"]);

        canonical.Should().BeTrue("'Part IV' is a canonical SEC 10-K section heading");
        prefixedWord
            .Should()
            .BeFalse("'Participants' merely starts with 'Part' and is not a section heading");
    }
}
