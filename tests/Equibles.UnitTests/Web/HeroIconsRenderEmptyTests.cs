using Equibles.Web.TagHelpers;

namespace Equibles.UnitTests.Web;

/// <summary>
/// Sibling to <see cref="HeroIconsTests"/>. Pins the early-return path in
/// <c>Render</c>: when <c>Get</c> returns empty (icon name not in the
/// dictionary), Render must emit an empty string — not a malformed
/// <c>&lt;svg&gt;</c> element with an empty <c>d</c> attribute. The malformed
/// element would render as an invisible 0×0 box that still consumes layout
/// space, silently breaking page alignment when an icon name is mistyped.
/// </summary>
public class HeroIconsRenderEmptyTests
{
    [Fact]
    public void Render_UnknownIconName_ReturnsEmptyStringInsteadOfMalformedSvg()
    {
        var html = HeroIcons.Render(
            "this-icon-does-not-exist",
            HeroIcons.IconStyle.Outline,
            size: "6"
        );

        html.Should().BeEmpty();
    }
}
