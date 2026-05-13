using System.Text.Encodings.Web;
using Equibles.Web.TagHelpers;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace Equibles.UnitTests.Web;

public class HeroIconTagHelperTests {
    [Fact]
    public void Process_SolidAttributeOnKnownIcon_RendersInlineSvgWithoutSurroundingIconTag() {
        // The companion UnknownIconName test only exercises the outline-default
        // suppress path. This pins three things the tag helper must do for a
        // known icon when `solid="true"` is set: (1) take the Solid branch of
        // the Outline/Solid ternary, (2) wipe `output.TagName` so the rendered
        // SVG isn't wrapped in a stray <icon> element the browser would treat
        // as a custom tag, and (3) inject the SVG via SetHtmlContent. A
        // refactor that drops the ternary or keeps the wrapper element would
        // emit broken markup, but only on solid-style usages — easy to miss
        // visually until a page renders.
        var sut = new HeroIconTagHelper { Name = "plus", Solid = true };
        var context = new TagHelperContext(
            new TagHelperAttributeList(),
            new Dictionary<object, object>(),
            uniqueId: "test");
        var output = new TagHelperOutput(
            "icon",
            new TagHelperAttributeList(),
            getChildContentAsync: (useCachedResult, encoder) =>
                Task.FromResult<TagHelperContent>(new DefaultTagHelperContent()));

        sut.Process(context, output);

        output.TagName.Should().BeNull();
        var content = output.Content.GetContent(HtmlEncoder.Default);
        content.Should().Contain("<svg");
        content.Should().Contain("fill=\"currentColor\"");
    }

    [Fact]
    public void Process_KnownIconWithDefaultSolidFalse_RendersOutlineStyledSvgWithStrokeAndFillNone() {
        // Sibling pin to Process_SolidAttributeOnKnownIcon. The existing solid pin
        // asserts that with `solid="true"`, the rendered SVG carries
        // `fill="currentColor"` — the marker of the Solid style. It says NOTHING
        // about the DEFAULT path (Solid=false), which is the path every
        // `<icon name="..."/>` tag in the codebase takes (no `solid` attribute is
        // the norm — solid is the rare opt-in for inline accent icons in toasts
        // and buttons).
        //
        // The Process method's branching is:
        //   var style = Solid ? HeroIcons.IconStyle.Solid : HeroIcons.IconStyle.Outline;
        // And HeroIcons.Render emits two completely different SVG attribute sets
        // based on `isSolid = style == IconStyle.Solid`:
        //   • Solid:   fill="currentColor", no stroke attributes
        //   • Outline: fill="none", stroke="currentColor", stroke-width="1.5",
        //              stroke-linecap="round", stroke-linejoin="round"
        // These are visually meaningful: outline icons render as thin-line glyphs;
        // solid icons render as filled shapes. A swap reskins the entire UI.
        //
        // The risk this catches is asymmetric and unreachable from the solid sibling
        // alone: a refactor that hardcodes `var style = HeroIcons.IconStyle.Solid`
        // (e.g. someone "cleaning up the Solid property" during a defaults review)
        // compiles cleanly, passes the existing solid pin (Solid=true still
        // resolves to Solid via hardcode), passes the Unknown pin (whose Render
        // returns "" before the style check matters), and silently rebrands every
        // outline icon — the entire site's icon set — as solid. The visual
        // symptom is uniform but subtle: same icon shapes, but every header,
        // nav-bar, button, and inline glyph gets a heavier filled appearance.
        // Operators rarely notice because the change happens to every icon
        // simultaneously and looks intentional.
        //
        // Equally, a regression that inverts the ternary
        // (`Solid ? Outline : Solid`) would be caught by the existing solid pin
        // (it asserts fill="currentColor" which would no longer appear) — so
        // this isn't the case to protect against. The case THIS pin uniquely
        // catches is the "always-solid" hardcode, which slips past every existing
        // test in this file.
        //
        // Pin: known icon, no `Solid` set (defaults to false), assert that the
        // rendered SVG carries the outline markers (fill="none" AND
        // stroke="currentColor"). Both attributes are required because
        // fill="none" alone could result from a constant-empty regression in
        // Render, while stroke="currentColor" alone is the load-bearing visual
        // signal — the stroke is what draws the visible outline shape.
        var sut = new HeroIconTagHelper { Name = "plus" };
        var context = new TagHelperContext(
            new TagHelperAttributeList(),
            new Dictionary<object, object>(),
            uniqueId: "test");
        var output = new TagHelperOutput(
            "icon",
            new TagHelperAttributeList(),
            getChildContentAsync: (useCachedResult, encoder) =>
                Task.FromResult<TagHelperContent>(new DefaultTagHelperContent()));

        sut.Process(context, output);

        output.TagName.Should().BeNull();
        var content = output.Content.GetContent(HtmlEncoder.Default);
        content.Should().Contain("<svg");
        content.Should().Contain("fill=\"none\"");
        content.Should().Contain("stroke=\"currentColor\"");
    }

    [Fact]
    public void Process_UnknownIconName_SuppressesOutput() {
        var sut = new HeroIconTagHelper { Name = "this-icon-does-not-exist" };
        var context = new TagHelperContext(
            new TagHelperAttributeList(),
            new Dictionary<object, object>(),
            uniqueId: "test");
        var output = new TagHelperOutput(
            "icon",
            new TagHelperAttributeList(),
            getChildContentAsync: (useCachedResult, encoder) =>
                Task.FromResult<TagHelperContent>(new DefaultTagHelperContent()));

        sut.Process(context, output);

        output.TagName.Should().BeNull();
        output.IsContentModified.Should().BeTrue();
        output.Content.GetContent(HtmlEncoder.Default).Should().BeEmpty();
    }
}
