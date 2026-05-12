using System.Text.RegularExpressions;
using Equibles.Web.Extensions;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc.Rendering;
using NSubstitute;

namespace Equibles.UnitTests.Web;

public class MarkdownExtensionsTests {
    [Fact]
    public void MarkdownToHtml_PipeTableWithBlankLineBetweenRows_RendersAsSingleTable() {
        string captured = null;
        var htmlHelper = Substitute.For<IHtmlHelper>();
        htmlHelper.Raw(Arg.Do<string>(s => captured = s))
            .Returns(callInfo => new HtmlString(callInfo.Arg<string>()));

        var markdown = "| H1 | H2 |\n|----|----|\n| a  | b  |\n\n| c  | d  |\n";

        htmlHelper.MarkdownToHtml(markdown);

        captured.Should().NotBeNull();
        Regex.Matches(captured, "<table").Count.Should().Be(1);
    }
}
