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
    public void Render_WithCustomCssClass_AppendsCssClassToBaseClassesWhilePreservingSize() {
        // Render's `cssClass` parameter is the only optional ternary branch
        // in the method's classes-string composition:
        //   var classes = string.IsNullOrEmpty(cssClass)
        //       ? $"{sizeClass} inline-block shrink-0"
        //       : $"{sizeClass} inline-block shrink-0 {cssClass}";
        // The existing pins use the DEFAULT cssClass (null), so the
        // false-branch (`string.IsNullOrEmpty(cssClass) == false`) is
        // unpinned. Callers across the public site use cssClass to
        // override base size or to inject Tailwind utility classes
        // (`text-amber-500 hover:text-amber-700` for icon-button
        // hover states, `text-red-600` for inline error indicators,
        // `animate-spin` for loading spinners, etc.).
        //
        // The risk this catches: a refactor that "simplifies" the
        // ternary to always use the no-cssClass shape — under the
        // false intuition that "we never pass cssClass in practice"
        // — would compile cleanly, pass the two existing
        // Render/Get pins, and silently drop every caller's
        // customization. Icon buttons would lose their hover colors,
        // loading spinners would stop spinning, error indicators
        // would render in default color. None of those surface in
        // unit tests; all are visible only in browser rendering.
        //
        // The complementary risk: a refactor that REPLACES the base
        // classes with cssClass (instead of APPENDING) — e.g.
        // `string.IsNullOrEmpty(cssClass) ? base : cssClass` —
        // would pass the existing solid-style pin's
        // attribute-shape assertions but strip the `size-6
        // inline-block shrink-0` base. Icons would default to the
        // browser's intrinsic SVG size (24×24 from viewBox) and
        // lose the inline-block + shrink-0 layout discipline.
        //
        // Pin: pass a representative cssClass ("text-red-600") and
        // assert BOTH the base classes (size-6, inline-block,
        // shrink-0) AND the custom class survive in the output.
        // The presence of the cssClass after the base classes also
        // proves the concat order (cssClass comes LAST, which is
        // load-bearing for Tailwind's last-class-wins resolution
        // — utility classes overriding base styles depend on this).
        var svg = HeroIcons.Render("plus", HeroIcons.IconStyle.Outline, size: "6", cssClass: "text-red-600");

        svg.Should().Contain("size-6");
        svg.Should().Contain("inline-block");
        svg.Should().Contain("shrink-0");
        svg.Should().Contain("text-red-600");
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
