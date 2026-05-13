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

    [Fact]
    public void Render_WithNonDefaultSize_EmitsCorrespondingSizeClass() {
        // Sibling to Render_WithCustomCssClass_AppendsCssClassToBaseClassesWhilePreservingSize.
        // The existing custom-cssClass pin asserts `svg.Should().Contain("size-6")`
        // but uses `size: "6"` — the DEFAULT. That pin proves the size class is
        // present when the default is selected, but it CANNOT distinguish the
        // correct interpolation:
        //     var sizeClass = $"size-{size}";
        // from a regression that hardcoded the size literal:
        //     var sizeClass = "size-6";  // hardcoded — bug
        // Both versions emit "size-6" when called with the default — the existing
        // pin's `Contain("size-6")` assertion passes either way.
        //
        // The risk this pin uniquely catches: a refactor that drops the `{size}`
        // interpolation in favor of a hardcoded literal — perhaps under the
        // false intuition that "we always pass size: 6 in practice, the
        // parameter is dead" — would compile cleanly, pass every existing
        // Render pin (Solid path, custom-cssClass, Get fallback), and silently
        // shrink/grow every icon that callers wanted at a non-default size.
        //
        // Real production use:
        //   • size: "4" — small inline icons in toast messages and badges
        //   • size: "5" — buttons in tables and lists
        //   • size: "6" — DEFAULT — body-text-adjacent icons (header nav, etc.)
        //   • size: "8" — feature card headers
        //   • size: "10" / "12" — hero illustrations and empty-state placeholders
        // A hardcoded "size-6" regression silently flattens all those sizes to
        // body-text size — the affordance gradient is gone, every icon becomes
        // the same visual weight. Operators don't notice immediately because
        // every icon still renders; they only notice when the visual hierarchy
        // looks "flat" or "off" — exactly the kind of degradation that
        // accumulates over time without an obvious trigger.
        //
        // The complementary risk: a refactor that prefixed differently
        // (`text-{size}` instead of `size-{size}` — a copy-paste from Tailwind's
        // text utility) would also break, producing `text-8` (not a valid
        // Tailwind utility) instead of `size-8` (the correct dimension utility).
        // Asserting on the exact "size-8" literal catches this too.
        //
        // Pin: pass `size: "8"` (a real non-default size used in production
        // for feature card headers) and assert the output contains the
        // CORRESPONDING "size-8" class AND does NOT contain "size-6" (the
        // hardcoded-regression marker). Both assertions are required — the
        // positive proves the interpolation works, the negative proves the
        // interpolation USED THE PARAMETER (not the default).
        var svg = HeroIcons.Render("plus", HeroIcons.IconStyle.Outline, size: "8");

        svg.Should().Contain("size-8");
        svg.Should().NotContain("size-6");
    }
}
