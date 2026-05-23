using Equibles.Web.TagHelpers;

namespace Equibles.UnitTests.Web;

// Lane A (adversarial): Get's contract is "look up by name+style; if the
// requested style doesn't exist, fall back to the outline variant." The
// sibling Render test only exercises an icon that has both variants, so the
// fallback branch is untested. A refactor that drops the GetValueOrDefault
// fallback (e.g. replacing the ternary with a direct TryGetValue return)
// would silently turn every outline-only icon invisible when requested as
// solid — the tag helper renders nothing for an empty path.
public class HeroIconsGetSolidFallbackTests
{
    [Fact]
    public void Get_SolidRequestedButOnlyOutlineExists_ReturnsOutlinePathData()
    {
        // "circle-stack" has only an outline variant in the Icons dictionary.
        var outlinePath = HeroIcons.Get("circle-stack", HeroIcons.IconStyle.Outline);
        var solidPath = HeroIcons.Get("circle-stack", HeroIcons.IconStyle.Solid);

        outlinePath.Should().NotBeNullOrEmpty();
        solidPath.Should().Be(outlinePath);
    }
}
