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
