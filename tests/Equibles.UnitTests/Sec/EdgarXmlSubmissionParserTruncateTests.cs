using Equibles.Sec.HostedService.Helpers;

namespace Equibles.UnitTests.Sec;

public class EdgarXmlSubmissionParserTruncateTests
{
    // Contract (doc-comment): Truncate caps an oversized free-text value at its destination
    // column's MaxLength so an over-long field can't fail the whole filing's INSERT. A value
    // longer than the cap must be cut to EXACTLY maxLength characters, preserving the prefix —
    // not maxLength-1, and never throwing. Oracle from the contract, not the body.
    [Fact]
    public void Truncate_ValueLongerThanMax_CapsToExactlyMaxLength()
    {
        var result = EdgarXmlSubmissionParser.Truncate("ABCDEF", 4);

        result.Should().Be("ABCD");
    }
}
