using Equibles.Holdings.HostedService.Services;

namespace Equibles.IntegrationTests.Holdings;

public class Filing13FXmlParserEmptyAmendmentTests
{
    [Fact]
    public void ParseInformationTable_WellFormedRootWithNoRows_ReturnsEmptyListAndDoesNotThrow()
    {
        // Contract (method XML-doc): ParseInformationTable "tolerates a missing
        // or empty table (an amendment that removes every position) by returning
        // an empty list." This is the canonical shape for that case — a real
        // amendment that zeros out every position ships a well-formed
        // <informationTable> with zero children. Both the direct-children scan
        // and the recursive-descent fallback must come up empty without
        // throwing. A refactor that drops the rows.Count == 0 fallback (or
        // that .Single()s the rows) would crash the importer on every such
        // amendment and silently lose the de-listing signal.
        const string xml = """
            <informationTable xmlns="http://www.sec.gov/edgar/document/thirteenf/informationtable">
            </informationTable>
            """;

        var holdings = new Filing13FXmlParser().ParseInformationTable(xml);

        holdings.Should().BeEmpty();
    }
}
