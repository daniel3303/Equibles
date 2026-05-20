using System.Reflection;
using Equibles.Holdings.HostedService.Services;

namespace Equibles.UnitTests.Holdings;

public class Filing13FXmlParserParseSecDateIsoFallbackTests
{
    // Adversarial: ParseSecDate's XML-doc-comment promises "Cover-page dates are
    // MM-DD-YYYY; a few historical filings use ISO." The MM-DD-YYYY branch is
    // pinned elsewhere; the ISO fallback (the second TryParse) is not. A refactor
    // that drops the general-TryParse fallback would compile cleanly and silently
    // return DateOnly.MinValue for those historical filings — the importer treats
    // MinValue as the "skip this filing" sentinel, so every ISO-dated filing
    // would vanish without an error.
    [Fact]
    public void ParseSecDate_IsoFormat_ParsesViaGeneralTryParseFallback()
    {
        var parse = typeof(Filing13FXmlParser).GetMethod(
            "ParseSecDate",
            BindingFlags.NonPublic | BindingFlags.Static
        )!;

        var result = (DateOnly)parse.Invoke(null, ["2024-09-30"])!;

        result.Should().Be(new DateOnly(2024, 9, 30));
    }
}
