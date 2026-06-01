using System.Globalization;
using Equibles.Sec.HostedService.Helpers;

namespace Equibles.UnitTests.Sec;

public class EdgarXmlSubmissionParserParseDateTests
{
    [Fact]
    public void ParseDate_SlashFormatUnderNonInvariantCulture_ParsesViaInvariantSeparator()
    {
        // Contract: parse against the supplied exact formats in INVARIANT culture. The '/' in
        // "MM/dd/yyyy" (the format real Form 144 / Form D filings use) is the culture's DateSeparator
        // placeholder — under de-DE that separator is '.', so a literal-slash value would fail to
        // match unless parsing is pinned to invariant. Oracle: the doc-comment, not the body.
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");

            var result = EdgarXmlSubmissionParser.ParseDate("03/15/2024", ["MM/dd/yyyy"]);

            result.Should().Be(new DateOnly(2024, 3, 15));
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }
}
