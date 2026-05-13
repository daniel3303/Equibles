using System.Text.RegularExpressions;
using Equibles.Web.Extensions;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc.Rendering;
using NSubstitute;

namespace Equibles.UnitTests.Web;

public class MarkdownExtensionsTests {
    [Fact]
    public void MarkdownToHtml_RawHtmlInInput_IsStrippedByDisableHtmlPipeline() {
        // The markdown rendered through this extension can originate from SEC filings,
        // earnings-call transcripts, and other ingested content that may contain raw
        // HTML — including potentially malicious payloads. The pipeline builder calls
        // `.DisableHtml()` specifically to prevent any `<script>`, `<iframe>`, or other
        // raw-HTML content in the source markdown from being passed through to the
        // page DOM. Markdig honours that flag by escaping HTML angle brackets at
        // render time. A regression that drops `.DisableHtml()` (e.g. someone copying
        // a different pipeline preset) would re-enable raw HTML passthrough, turning
        // any ingested document into a vector for stored XSS the next time it
        // renders in the Web project. Pin the strip on a `<script>` payload — assert
        // the rendered HTML does NOT contain a live `<script` tag (only the escaped
        // form `&lt;script&gt;` which is inert).
        string captured = null;
        var htmlHelper = Substitute.For<IHtmlHelper>();
        htmlHelper.Raw(Arg.Do<string>(s => captured = s))
            .Returns(callInfo => new HtmlString(callInfo.Arg<string>()));

        var markdown = "Hello <script>alert('xss')</script> world";

        htmlHelper.MarkdownToHtml(markdown);

        captured.Should().NotBeNull();
        captured.Should().NotContain("<script>");
        captured.Should().Contain("&lt;script&gt;");
    }

    [Fact]
    public void MarkdownToHtml_NullInput_ShortCircuitsToEmptyContentWithoutInvokingMarkdig() {
        // Views render Markdown for stored content that may be absent (a missing
        // field, an unsaved record). The extension short-circuits null/empty
        // input to Raw(string.Empty) BEFORE building the Markdig pipeline —
        // skip that guard and Markdig's Markdown.ToHtml throws NRE on a null
        // input, surfacing as a runtime error far from the helper call. Pin
        // the early return on null so a regression that drops the guard
        // fails this test instead.
        string captured = null;
        var htmlHelper = Substitute.For<IHtmlHelper>();
        htmlHelper.Raw(Arg.Do<string>(s => captured = s))
            .Returns(callInfo => new HtmlString(callInfo.Arg<string>()));

        var result = htmlHelper.MarkdownToHtml(null);

        result.Should().NotBeNull();
        captured.Should().Be(string.Empty);
    }

    [Fact]
    public void MarkdownToHtml_PipeTableWithCascadedBlankLinesBetweenRows_RendersAsSingleTable() {
        // The existing single-blank-line pin proves the BlankLineBetweenPipeRows
        // regex replacement works for ONE adjacent pair. The production code
        // wraps that Replace in a do-while loop until the input stops changing:
        //   do { previous = markdown; markdown = regex.Replace(...); }
        //   while (markdown != previous);
        // The loop is load-bearing for inputs with THREE OR MORE pipe rows
        // separated by blank lines — a single .Replace pass non-overlappingly
        // matches alternating pairs, so a 3-row input
        //   row1\n\nrow2\n\nrow3
        // becomes after one pass:
        //   row1\nrow2\n\nrow3
        // (only the first pair collapsed; the second blank line survives).
        // Only the next iteration finishes the job.
        //
        // The risk this catches: a refactor that "tidies up" the do-while
        // into a single Replace call — under the false intuition that the
        // regex's `g`-style replacement already handles all matches —
        // would compile, pass the existing single-pair pin, and silently
        // leave middle blank lines intact in any pipe table with 3+ rows
        // separated by blank lines. Markdig then renders the cluster as
        // multiple SEPARATE tables (one per non-blank stretch), breaking
        // the visual continuity of historical-data tables we've stored in
        // legacy SEC ingest formats (where the upstream HTML→Markdown
        // converter routinely emitted blank-line-separated rows).
        //
        // Pin a 3-row pipe table with blank lines between EVERY row.
        // Assert the rendered HTML contains exactly ONE <table tag —
        // multiple tables would mean a cascading collapse failed. The
        // single-pair pin's regex one-pass replacement is insufficient
        // for this case; only the do-while loop's convergence produces
        // a single table.
        string captured = null;
        var htmlHelper = Substitute.For<IHtmlHelper>();
        htmlHelper.Raw(Arg.Do<string>(s => captured = s))
            .Returns(callInfo => new HtmlString(callInfo.Arg<string>()));

        var markdown = "| H1 | H2 |\n|----|----|\n| a  | b  |\n\n| c  | d  |\n\n| e  | f  |\n";

        htmlHelper.MarkdownToHtml(markdown);

        captured.Should().NotBeNull();
        Regex.Matches(captured, "<table").Count.Should().Be(1);
    }

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
