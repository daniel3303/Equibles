using Equibles.Web.TagHelpers;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Metadata;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace Equibles.UnitTests.Web;

public class StatusBadgeTagHelperZeroCountTests
{
    // Contract (HtmlTargetElement "status-badge" + the guard's intent): the
    // badge is an alert counter for the nav, so it must render ONLY when there
    // is at least one alert. A StatusBadgeCount of 0 means "nothing to show" —
    // the helper must suppress its output entirely, never emit a "0" badge.
    // The boundary is the bait: an off-by-one guard (count < 0 instead of <= 0)
    // would leak a zero badge into every page that sets the count to 0.
    [Fact]
    public void Process_StatusBadgeCountIsZero_SuppressesOutput()
    {
        var viewData = new ViewDataDictionary(
            new EmptyModelMetadataProvider(),
            new ModelStateDictionary()
        )
        {
            ["StatusBadgeCount"] = 0,
        };
        var sut = new StatusBadgeTagHelper
        {
            ViewContext = new ViewContext { ViewData = viewData },
        };
        var context = new TagHelperContext(
            new TagHelperAttributeList(),
            new Dictionary<object, object>(),
            "test-id"
        );
        var output = new TagHelperOutput(
            "status-badge",
            new TagHelperAttributeList(),
            (_, _) => Task.FromResult<TagHelperContent>(new DefaultTagHelperContent())
        );

        sut.Process(context, output);

        // SuppressOutput() nulls the tag name and clears content — no span, no "0".
        output.TagName.Should().BeNull();
        output.Content.GetContent().Should().BeEmpty();
    }
}
