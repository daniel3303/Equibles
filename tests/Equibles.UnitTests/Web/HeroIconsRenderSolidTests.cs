using Equibles.Web.TagHelpers;

namespace Equibles.UnitTests.Web;

// Lane B (coverage): exercises the solid branch of HeroIcons.Render — the
// sibling test covers outline only. Solid SVGs use fill="currentColor" and
// omit stroke attributes; a regression that hardcodes the outline attributes
// would produce double-rendered icons (filled AND stroked).
public class HeroIconsRenderSolidTests
{
    [Fact]
    public void Render_SolidIcon_ProducesSvgWithFillAndNoStroke()
    {
        var svg = HeroIcons.Render("plus", HeroIcons.IconStyle.Solid);

        svg.Should().Contain("fill=\"currentColor\"");
        svg.Should().NotContain("stroke=");
        svg.Should().NotContain("stroke-linecap");
        svg.Should().Contain("class=\"size-6 inline-block shrink-0\"");
    }
}
