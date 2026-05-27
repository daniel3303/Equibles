using System.IO;
using System.Text.Encodings.Web;
using Equibles.Web.TagHelpers;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace Equibles.UnitTests.Web;

public class EmptyTableRowTagHelperProcessTests
{
    // Contract (HtmlTargetElement "empty-row" + the Process body shape):
    // <empty-row colspan="N">inner text</empty-row> in a Razor view must
    // render to a <tr> whose body is a single <td> spanning N columns,
    // centred and muted, wrapping whatever inner content the caller put
    // in the source. The class string is the DaisyUI v5 convention used
    // across the project's empty-state tables.
    //
    // Risk surface this pin uniquely catches (zero existing coverage):
    //   • TagName swap — `output.TagName = "tr"` is what causes the
    //     element to render as a table row. A refactor that "tidies"
    //     the assignment to "td" (an intuitive collapse — "we only
    //     emit a <td> so why double-wrap?") would compile, but
    //     browsers reject <td> outside <tr> and the empty-state row
    //     would silently disappear from every empty-table view.
    //
    //   • Attributes.Clear drop — a refactor that removes
    //     `output.Attributes.Clear()` would let any caller-supplied
    //     attribute (e.g. a stray `class="bg-red"` on the <empty-row>
    //     source element) leak onto the <tr>, breaking the empty-
    //     state styling.
    //
    //   • Pre-content/post-content boundary — the <td> open tag is
    //     in PreContent, the </td> close is in PostContent. Razor
    //     concatenates Pre + Children + Post. If the open and close
    //     tag landed in the same slot (a refactor that consolidated
    //     them into a single SetHtmlContent call wrapping the body),
    //     the <td> would self-close empty and the caller-supplied
    //     inner content would float outside the cell — visually the
    //     row would render empty and the message would appear in the
    //     wrong column. Asserting both Pre and Post separately catches
    //     this collapse.
    //
    //   • Class-string typo — the literal "text-center py-10
    //     text-base-content/60" must match the DaisyUI utility
    //     classes the rest of the project uses for empty-state
    //     muted-centred text. A typo (e.g. "text-base-content/40"
    //     from a copy-paste edit on the opacity suffix, or
    //     "py-8" instead of "py-10" from a refactor that
    //     "harmonised" spacing) would produce a subtly-off visual
    //     that integration tests rarely catch.
    //
    //   • Colspan interpolation — `$"{Colspan}"` uses the default
    //     int-to-string conversion under the THREAD culture. C#'s
    //     interpolation respects InvariantCulture for int by default
    //     (since .NET 6+), so a non-en-US culture (e.g. de-DE which
    //     uses thousands-separators in some formats) is safe. But a
    //     refactor that "explicitly" formatted as
    //     `Colspan.ToString("N0", CultureInfo.CurrentCulture)` under
    //     the false intuition that "we should respect culture for
    //     numeric formatting" would produce `colspan="1,234"` for a
    //     hypothetically-large colspan — invalid HTML and the row
    //     would render at colspan=1 (HTML's parser stops at the
    //     first non-digit). Pin a multi-digit colspan to surface
    //     this regression. 12 is enough to prove single-digit and
    //     multi-digit paths both work without paying for a
    //     thousands-separator test (no real table has 1000+
    //     columns).
    //
    // Pin: invoke Process with Colspan=12; assert the full structural
    // contract via the rendered Pre/Post HTML strings and the TagName
    // / Attributes properties. The dual Pre+Post assertion is the
    // bait for the open/close consolidation regression.
    [Fact]
    public void Process_RendersTrWrappingTdWithColspanAndEmptyStateClass()
    {
        var sut = new EmptyTableRowTagHelper { Colspan = 12 };
        var context = new TagHelperContext(
            new TagHelperAttributeList(),
            new Dictionary<object, object>(),
            "test-id"
        );
        var output = new TagHelperOutput(
            "empty-row",
            new TagHelperAttributeList { { "stale", "should-be-cleared" } },
            (_, _) => Task.FromResult<TagHelperContent>(new DefaultTagHelperContent())
        );

        sut.Process(context, output);

        output.TagName.Should().Be("tr");
        output.TagMode.Should().Be(TagMode.StartTagAndEndTag);
        output.Attributes.Should().BeEmpty();
        Render(output.PreContent)
            .Should()
            .Be("<td colspan=\"12\" class=\"text-center py-10 text-base-content/60\">");
        Render(output.PostContent).Should().Be("</td>");
    }

    private static string Render(TagHelperContent content)
    {
        using var writer = new StringWriter();
        content.WriteTo(writer, HtmlEncoder.Default);
        return writer.ToString();
    }
}
