using Equibles.Web.TagHelpers;

namespace Equibles.UnitTests.Web;

public class HeroIconsTests {
    [Fact]
    public void Get_SolidStyleMissingButOutlineExists_FallsBackToOutlinePath() {
        var solid = HeroIcons.Get("circle-stack", HeroIcons.IconStyle.Solid);
        var outline = HeroIcons.Get("circle-stack", HeroIcons.IconStyle.Outline);

        outline.Should().NotBeEmpty();
        solid.Should().Be(outline);
    }

    [Fact]
    public void Render_SolidStyle_EmitsFilledCurrentColorWithoutStrokeAttributes() {
        // Render's solid path differs from outline at three points: fill, stroke,
        // and the path's stroke-linecap/stroke-linejoin attrs. Outline icons use
        // a transparent fill with a colored stroke; solid icons use a colored
        // fill and no stroke at all. A refactor that flips the ternaries (or
        // merges the two styles into one render path) would silently change
        // every solid icon to render as an outline glyph, breaking the visual
        // affordance for filled-state buttons. Pin the solid attribute shape
        // so the regression fails at test time.
        var svg = HeroIcons.Render("plus", HeroIcons.IconStyle.Solid);

        svg.Should().Contain("fill=\"currentColor\"");
        svg.Should().NotContain("stroke=\"currentColor\"");
        svg.Should().NotContain("stroke-linecap");
    }
}
