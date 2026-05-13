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
    public void SpanWithNestedBoldChild_InnerHtmlBranchTriggersH3Conversion() {
        // IsBoldSpan is two independent OR-arms:
        //   (a) span's own `style` attribute contains "font-weight:bold"
        //   (b) span's `innerHtml` (the serialized markup of its children) contains
        //       "font-weight:bold" — i.e. the bold styling lives on a NESTED element
        //       like <font style="font-weight:bold"> rather than on the span itself.
        // The existing `BoldSpan_IsConvertedToH3` pin exercises arm (a) only. Arm (b)
        // is unpinned, and it's not redundant: SEC filings emitted by Workiva, Donnelley
        // Financial, and Toppan Merrill routinely wrap section headings as
        //   <span><font style="font-weight:bold">Revenue</font></span>
        // because the upstream Word→XBRL conversion bubbles formatting onto the inner
        // <font> element rather than the wrapping span. Without the innerHtml fallback,
        // every such heading would skip H3 promotion and stay as a regular span,
        // wrecking the heading hierarchy that chunk-by-heading + table-of-contents
        // extraction depend on. A refactor that "simplifies" IsBoldSpan to read only
        // the span's own style attribute would compile cleanly, pass the existing
        // BoldSpan test, and silently demote half the production filing corpus.
        //
        // Pin the innerHtml branch with a nested <font> bold child. The promotion to
        // H3 confirms IsBoldSpan returned true via arm (b) — `AllSiblingsMatch` only
        // succeeds if every meaningful sibling passes the predicate, and there's only
        // one span here, so the H3 output is a direct signal that the innerHtml-bold
        // check fired.
        var result = Execute("<div><span><font style=\"font-weight:bold\">Revenue</font></span></div>");

        result.Should().Contain("<h3>");
        result.Should().Contain("Revenue");
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
    public void SpanWithNestedItalicChild_InnerHtmlBranchTriggersH4Conversion() {
        // IsItalicSpan mirrors the dual-arm structure of IsBoldSpan:
        //   (a) span's own `style` attribute contains "font-style:italic"
        //   (b) span's `innerHtml` (the serialized markup of its children)
        //       contains "font-style:italic" — i.e. the italic styling lives
        //       on a NESTED element like <font style="font-style:italic">
        //       rather than on the span itself.
        // The existing `ItalicSpan_IsConvertedToH4` pin exercises arm (a) only.
        // Arm (b) is unpinned — and the parallel `SpanWithNestedBoldChild`
        // already proves bold's innerHtml fallback matters in practice: SEC
        // filings emitted by Workiva, Donnelley Financial, and Toppan Merrill
        // routinely bubble formatting onto inner <font> elements rather than
        // the wrapping span. The same upstream Word→XBRL conversion pattern
        // applies to italic styling (forward-looking statement notes,
        // footnote-pointer text, "see Note 3" cross-references). Without
        // the italic innerHtml fallback every such note would skip H4
        // promotion and stay as a regular span. A refactor that
        // "simplifies" IsItalicSpan to read only the span's own style
        // attribute — the exact mistake the bold pin guards against —
        // would compile cleanly, pass the existing ItalicSpan test, and
        // silently demote italic-formatted notes across the production
        // filing corpus. Pin the italic innerHtml branch with a nested
        // <font> italic child; the H4 promotion confirms IsItalicSpan
        // returned true via arm (b).
        var result = Execute("<div><span><font style=\"font-style:italic\">Note: see Item 3</font></span></div>");

        result.Should().Contain("<h4>");
        result.Should().Contain("Note: see Item 3");
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
