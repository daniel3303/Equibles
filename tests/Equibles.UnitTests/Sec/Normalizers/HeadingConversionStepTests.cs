using AngleSharp.Html.Parser;
using Equibles.Sec.BusinessLogic.Normalizers;

namespace Equibles.UnitTests.Sec.Normalizers;

public class HeadingConversionStepTests {
    private readonly HeadingConversionStep _step = new();
    private readonly HtmlParser _parser = new();

    private string Execute(string bodyHtml) {
        var html = $"<html><body>{bodyHtml}</body></html>";
        var doc = _parser.ParseDocument(html);
        _step.Execute(doc);
        return doc.Body!.InnerHtml;
    }

    [Fact]
    public void PartHeading_IsConvertedToH1() {
        var result = Execute("<div><span>PART I</span></div>");

        result.Should().Contain("<h1>PART I</h1>");
    }

    [Fact]
    public void ItemHeading_IsConvertedToH2() {
        var result = Execute("<div><span>ITEM 1. Business</span></div>");

        result.Should().Contain("<h2>ITEM 1. Business</h2>");
    }

    [Fact]
    public void BoldSpan_IsConvertedToH3() {
        var result = Execute("<div><span style=\"font-weight:bold\">Revenue</span></div>");

        result.Should().Contain("<h3>Revenue</h3>");
    }

    [Fact]
    public void AllUppercaseSpan_IsConvertedToH3() {
        var result = Execute("<div><span>REVENUE</span></div>");

        result.Should().Contain("<h3>REVENUE</h3>");
    }

    [Fact]
    public void ItalicSpan_IsConvertedToH4() {
        var result = Execute("<div><span style=\"font-style:italic\">Note: Important</span></div>");

        result.Should().Contain("<h4>Note: Important</h4>");
    }

    [Fact]
    public void SpanInsideTable_IsNotConverted() {
        var result = Execute("<table><tr><td><span style=\"font-weight:bold\">Revenue</span></td></tr></table>");

        result.Should().NotContain("<h3>");
        result.Should().Contain("<span");
    }

    [Fact]
    public void RegularSpanWithoutStyling_IsNotConverted() {
        var result = Execute("<div><span>Some regular text here</span></div>");

        result.Should().NotContain("<h1>")
            .And.NotContain("<h2>")
            .And.NotContain("<h3>")
            .And.NotContain("<h4>");
    }

    [Fact]
    public void SpanInsideParentWithCenterAlignment_IsConvertedToH3() {
        // SEC filings frequently mark section labels by centering them at the
        // parent level (<div style="text-align:center"><span>Label</span></div>)
        // rather than on the span itself. IsCenterAligned reads both span and
        // parent styles; the parent-only path was previously unexercised — pin
        // it so centered section labels without bold/uppercase still get H3.
        var result = Execute("<div style=\"text-align:center\"><span>Section Title</span></div>");

        result.Should().Contain("<h3>Section Title</h3>");
    }

    [Fact]
    public void ParentheticalText_DoesNotTriggerHeading() {
        var result = Execute("<div><span>(continued)</span></div>");

        result.Should().NotContain("<h3>");
    }

    [Fact]
    public void PartHeadingWithArabicNumeral_DoesNotBecomeH1() {
        // SEC 10-K filings consistently use Roman numerals for PART headings —
        // PART I, PART II, PART III, PART IV. `IsPartHeading` enforces this by
        // checking `firstWord.All(char.IsLetter)` AFTER stripping the "PART "
        // prefix: "PART I" → firstWord "I" (all letters, true), "PART 1" →
        // firstWord "1" (non-letter, false). The H1 promotion is reserved for
        // the structural Roman-numeral PARTS only; an Arabic-numeral "PART 1"
        // in an arbitrary filing must NOT outrank an actual PART I in the
        // document outline. A refactor that drops `firstWord.All(char.IsLetter)`
        // would compile cleanly and pass the existing happy-path
        // `PartHeading_IsConvertedToH1` test, while silently double-promoting
        // every "PART 1" / "PART 2" string in a filing — wrecking the heading
        // hierarchy that downstream consumers (chunk-by-heading,
        // table-of-contents extraction) depend on. Pin the rejection with
        // an Arabic-numeral input; the existing uppercase fallback still
        // turns it into an H3, but never an H1.
        var result = Execute("<div><span>PART 1</span></div>");

        result.Should().NotContain("<h1>");
    }
}
