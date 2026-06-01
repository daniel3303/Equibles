using Equibles.Sec.HostedService.Helpers;

namespace Equibles.UnitTests.Sec;

public class EdgarXmlSubmissionParserCleanTests
{
    [Fact]
    public void Clean_WhitespaceWrappedLowercaseNotApplicable_ReturnsNull()
    {
        // Contract (doc-comment): "Trimmed text with the 'N/A' placeholder and blanks normalized to
        // null." A surrounding-whitespace, lowercase "n/a" must normalize to null — exercising both
        // trim-before-compare and case-insensitive placeholder matching. Oracle from the contract.
        var result = EdgarXmlSubmissionParser.Clean("  n/a  ");

        result.Should().BeNull();
    }
}
