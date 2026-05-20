using Equibles.Holdings.HostedService.Services;

namespace Equibles.UnitTests.Holdings;

public class Filing13FXmlParserNullInfoTableXmlTests
{
    [Fact]
    public void ParseInformationTable_NullXml_ReturnsEmptyListWithoutThrowing()
    {
        // Contract (method XML-doc on ParseInformationTable): "Tolerates a missing
        // or empty table … by returning an empty list." The realtime ingestion
        // sweep passes the artifact bytes through after a None/empty-string check
        // upstream — but the unit contract is the safety net. A regression that
        // dropped the IsNullOrWhiteSpace guard would NRE on XDocument.Parse(null)
        // and crash the worker on the first amendment that ships without an info
        // table. The existing empty-amendment pin covers the well-formed empty-root
        // path; the null/whitespace branch is the unpinned half of the same
        // promise.
        var parser = new Filing13FXmlParser();

        var act = () => parser.ParseInformationTable(null);

        act.Should().NotThrow();
        parser.ParseInformationTable(null).Should().BeEmpty();
    }
}
