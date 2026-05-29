using System.Reflection;
using Equibles.Integrations.Cboe;

namespace Equibles.UnitTests.Integrations;

public class CboeClientExtractOptionsDataJsonBraceInStringTests
{
    // CboeClient.ExtractOptionsDataJson scans the scraped page for the
    // `optionsData` object and returns the balanced `{...}` that follows.
    // Contract (a JSON object extractor): braces appearing inside a string
    // VALUE must not be treated as structural delimiters — the complete,
    // valid object must be returned regardless of a stray '}' in any field.
    //
    // The method carries inString/escaped tracking that is meant to enforce
    // exactly that. But the embedded blob is double-escaped (every quote is
    // `\"`), so each '"' is consumed by the escape branch and inString never
    // becomes true. A '}' inside a string value therefore decrements depth to
    // zero and truncates extraction to invalid JSON.
    [Fact(
        Skip = "GH-2722 — ExtractOptionsDataJson truncates JSON on a brace inside a string value"
    )]
    public void ExtractOptionsDataJson_BraceInsideStringValue_ReturnsCompleteObject()
    {
        var method = typeof(CboeClient).GetMethod(
            "ExtractOptionsDataJson",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        // Double-escaped page fragment: a label value contains a literal '}'.
        var html =
            "<script>x = {\"optionsData\\\":"
            + "{\\\"label\\\":\\\"a}b\\\",\\\"value\\\":\\\"0.88\\\"}};</script>";

        var result = (string)method!.Invoke(null, [html]);

        result.Should().Be("{\"label\":\"a}b\",\"value\":\"0.88\"}");
    }
}
