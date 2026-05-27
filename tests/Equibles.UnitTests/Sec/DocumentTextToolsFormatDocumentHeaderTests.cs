using System.Reflection;
using Equibles.CommonStocks.Data.Models;
using Equibles.Sec.Data.Models;
using Equibles.Sec.Mcp.Tools;

namespace Equibles.UnitTests.Sec;

public class DocumentTextToolsFormatDocumentHeaderTests
{
    // FormatDocumentHeader is the per-document banner every MCP
    // ReadDocumentLines / SearchDocumentKeyword response opens with.
    // The contract is the interpolation:
    //   $"{Name} ({Ticker}) {DocumentType} filed {ReportingDate:yyyy-MM-dd}"
    //
    // The `:yyyy-MM-dd` format specifier is the load-bearing piece. If
    // dropped to `{ReportingDate}`, DateOnly.ToString() becomes culture-
    // sensitive — pt-PT renders "27/05/2026", en-US "5/27/2026" — and
    // the LLM consumer (and any regex-based parser downstream) keys on
    // the ISO `yyyy-MM-dd` shape to extract the filing date.
    //
    // Pin: invoke with a known stock, DocumentType.TenK (DisplayName
    // "10-K"), ReportingDate 2026-05-27. The exact expected output is
    // "Apple Inc. (AAPL) 10-K filed 2026-05-27". A single equality
    // assertion catches all four regression classes in one shot:
    //   • ISO date drop → culture-dependent date format.
    //   • Field-order swap → e.g. Ticker before Name, or date before
    //     DocumentType.
    //   • Parenthesis loss → "Apple Inc. AAPL 10-K…".
    //   • Literal "filed" replacement → "Apple Inc. (AAPL) 10-K dated
    //     2026-05-27".
    //
    // Reflection-invoke since the helper is private static.
    [Fact]
    public void FormatDocumentHeader_KnownDocument_RendersIsoDateAfterTickerAndType()
    {
        var document = new Document
        {
            CommonStock = new CommonStock { Name = "Apple Inc.", Ticker = "AAPL" },
            DocumentType = DocumentType.TenK,
            ReportingDate = new DateOnly(2026, 5, 27),
        };

        var method = typeof(DocumentTextTools).GetMethod(
            "FormatDocumentHeader",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var result = (string)method!.Invoke(null, [document]);

        result.Should().Be("Apple Inc. (AAPL) 10-K filed 2026-05-27");
    }
}
