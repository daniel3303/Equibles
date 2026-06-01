using System.Reflection;
using Equibles.Sec.Data.Models;
using Equibles.Sec.Mcp.Tools;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// Contract: ParseDocumentType maps a recognized type string to a DocumentType and
/// an unrecognized non-empty string to null (no type filter) — never throwing. The
/// `FromDisplayName(x) ?? FromValue(x)` composition silently depends on both lookups
/// returning null (not throwing) on no match; if either started throwing on unknown
/// input, every Search tool would crash whenever an LLM passes a slightly-off type
/// string. Oracle derived from that fallback contract before reading the body.
/// </summary>
public class RagSearchToolsParseDocumentTypeUnrecognizedTests
{
    [Fact]
    public void ParseDocumentType_UnrecognizedValue_ReturnsNullWithoutThrowing()
    {
        var method = typeof(RagSearchTools).GetMethod(
            "ParseDocumentType",
            BindingFlags.NonPublic | BindingFlags.Static
        )!;

        // A non-empty string matching no value or display name must degrade to
        // "no filter" (null), not raise — the ?? fallback relies on it.
        var result = (DocumentType)method.Invoke(null, ["definitely-not-a-document-type"])!;

        result
            .Should()
            .BeNull(
                "an unrecognized document-type filter must resolve to null (no filter) without throwing, since the parser composes two non-throwing lookups"
            );
    }
}
