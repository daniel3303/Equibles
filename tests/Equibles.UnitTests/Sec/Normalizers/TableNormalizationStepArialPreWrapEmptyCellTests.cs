using AngleSharp.Html.Parser;
using Equibles.Sec.BusinessLogic.Normalizers;

namespace Equibles.UnitTests.Sec.Normalizers;

public class TableNormalizationStepArialPreWrapEmptyCellTests
{
    // IsCellEmpty has FOUR distinct empty-cell signatures: NullOrWhiteSpace
    // text, the canonical `&nbsp;`/`&#160;` entity, a third VERY specific
    // magic-string match for an Arial pre-wrap span (a recurring SEC filer
    // artifact), and the IsOnlyWhitespaceSpan fallback. The first three are
    // structurally independent — the third sits between the `&nbsp;` arm and
    // the AngleSharp-reparsing fallback and is the ONLY hard-coded substring
    // probe of its kind in the normalizer. A refactor that "consolidated" the
    // magic-string check into the IsOnlyWhitespaceSpan fallback would change
    // the matching surface (Contains vs reparse) — that filer-emitted span
    // would have to round-trip through AngleSharp and survive IsMeaningfulText
    // with the exact same verdict, which is not guaranteed across versions.
    // Pin the magic-string arm so a future "cleanup" cannot silently let those
    // decorative spans leak into the rendered table.
    [Fact]
    public void Execute_RowWithOnlyArialPreWrapSpanCell_RemovesRow()
    {
        var parser = new HtmlParser(
            new HtmlParserOptions { IsAcceptingCustomElementsEverywhere = true }
        );
        var doc = parser.ParseDocument(
            "<html><body><table>"
                + "<tr><td><span style=\"white-space:pre-wrap;font-family:Arial;font-kerning:none;min-width:fit-content;\"> </span></td></tr>"
                + "<tr><td>Real content</td></tr>"
                + "</table></body></html>"
        );
        var step = new TableNormalizationStep(parser);

        step.Execute(doc);

        var rows = doc.QuerySelectorAll("tr");
        rows.Length.Should().Be(1);
        rows[0].TextContent.Trim().Should().Be("Real content");
    }
}
