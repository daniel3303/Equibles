using Equibles.Web.TagHelpers;

namespace Equibles.UnitTests.Web;

// Lane B (coverage): exercises HeroIcons.Render — entirely zero-hit today.
// Covers the outline path (stroke attributes, fill="none"), the size class
// injection, and the custom CSS class append.
public class HeroIconsRenderTests
{
    [Fact]
    public void Render_KnownOutlineIcon_ProducesWellFormedSvgWithStrokeAttributes()
    {
        var svg = HeroIcons.Render(
            "plus",
            HeroIcons.IconStyle.Outline,
            size: "5",
            cssClass: "text-red-500"
        );

        svg.Should().StartWith("<svg ");
        svg.Should().Contain("class=\"size-5 inline-block shrink-0 text-red-500\"");
        svg.Should().Contain("fill=\"none\"");
        svg.Should().Contain("stroke=\"currentColor\"");
        svg.Should().Contain("stroke-width=\"1.5\"");
        svg.Should().Contain("stroke-linecap=\"round\"");
        svg.Should().EndWith("</svg>");
    }
}
