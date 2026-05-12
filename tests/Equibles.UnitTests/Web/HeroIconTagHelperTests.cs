using System.Text.Encodings.Web;
using Equibles.Web.TagHelpers;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace Equibles.UnitTests.Web;

public class HeroIconTagHelperTests {
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
