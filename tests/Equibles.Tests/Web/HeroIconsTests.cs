using Equibles.Web.TagHelpers;

namespace Equibles.Tests.Web;

public class HeroIconsTests {
    [Fact]
    public void Get_SolidStyleMissingButOutlineExists_FallsBackToOutlinePath() {
        var solid = HeroIcons.Get("circle-stack", HeroIcons.IconStyle.Solid);
        var outline = HeroIcons.Get("circle-stack", HeroIcons.IconStyle.Outline);

        outline.Should().NotBeEmpty();
        solid.Should().Be(outline);
    }
}
