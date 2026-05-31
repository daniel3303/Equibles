using Equibles.InsiderTrading.BusinessLogic;

namespace Equibles.UnitTests.Sec;

public class InsiderTradingFilingProcessorSanitizeXmlDecimalNumericTests
{
    // Sibling to InsiderTradingFilingProcessorSanitizeXmlNumericEntityTests
    // (which pins the HEX numeric character reference branch `&#x..`).
    // The negative lookahead in the production regex
    //   @"&(?!(amp|lt|gt|quot|apos|#\d+|#x[\da-fA-F]+);)"
    // protects SIX kinds of well-formed escapes from double-escaping: five
    // named entities (amp/lt/gt/quot/apos), HEX numeric (`#x[\da-fA-F]+`),
    // and DECIMAL numeric (`#\d+`). The HEX arm is pinned by the existing
    // sibling; the DECIMAL arm is unpinned.
    //
    // The risk this pin uniquely catches:
    //   • A "tidy — only hex in practice" cleanup that drops `#\d+` from
    //     the negative lookahead would compile cleanly, pass the hex pin
    //     (`&#x4A;` still recognised), and silently double-escape every
    //     decimal entity in production filings (`&#65;` → `&amp;#65;`).
    //     The downstream XML parser would then render the literal text
    //     `&#65;` in the filing body — every dash, smart quote, and
    //     non-ASCII glyph emitted as a decimal entity by SEC's older
    //     XBRL pipelines would appear as raw escape text in the
    //     insider-transactions view.
    //   • A regex anchor regression (e.g. requiring the entity to start
    //     a line) — caught by the inline-position assertion shape.
    //
    // Pin: an inline decimal numeric entity (`&#65;` = "A") survives a
    // round-trip through SanitizeXml unchanged. The literal `&` MUST
    // NOT become `&amp;` (which would be `&amp;#65;`) — that's the
    // adversarial signal.
    [Fact]
    public void SanitizeXml_DecimalNumericCharacterReference_IsPreservedNotDoubleEscaped()
    {
        var result = InsiderFilingParser.SanitizeXml("<v>&#65;</v>");

        result.Should().Be("<v>&#65;</v>");
    }
}
