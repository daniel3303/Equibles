using Equibles.Sec.HostedService.Helpers;

namespace Equibles.UnitTests.Sec;

public class EdgarXmlSubmissionParserParseDateInvalidTests
{
    // Contract: ParseDate returns null when the value is "unparseable". A value that fits the
    // format SHAPE but names an impossible calendar day (Feb 30) is unparseable — TryParseExact
    // must reject it, never roll over to Mar 1 or throw. Oracle: the doc-comment, not the body.
    [Fact]
    public void ParseDate_CalendarInvalidDayMatchingFormatShape_ReturnsNull()
    {
        var result = EdgarXmlSubmissionParser.ParseDate("02/30/2024", ["MM/dd/yyyy"]);

        result.Should().BeNull();
    }
}
