using System.Reflection;
using Equibles.Holdings.HostedService.Services;

namespace Equibles.UnitTests.Holdings;

public class Realtime13FIngestionServiceOrderedInfoTableCandidatesNonXmlExcludedTests
{
    // OrderedInfoTableCandidates' doc-comment says "the information table is
    // one of the filing's non-cover .xml artifacts" — the filter is two-armed
    // (`EndsWith(".xml", IgnoreCase) && !Equals("primary_doc.xml", ...)`).
    // Sibling pins cover ordering and the primary_doc exclusion; this pins the
    // .xml-extension arm. A regression that simplified the filter to "anything
    // except primary_doc.xml" would silently leak rendering .htm / metadata
    // files into the candidate stream — the caller would feed them to the
    // XML parser as the info-table, which NREs on the first parse attempt.
    [Fact]
    public void OrderedInfoTableCandidates_HtmFileAlongsideXmlInfoTable_HtmExcluded()
    {
        List<string> artifacts = ["filing-summary.htm", "form13fInfoTable.xml"];

        var method = typeof(Realtime13FIngestionService).GetMethod(
            "OrderedInfoTableCandidates",
            BindingFlags.NonPublic | BindingFlags.Static
        );
        var result = ((IEnumerable<string>)method.Invoke(null, [artifacts])).ToList();

        result.Should().Equal("form13fInfoTable.xml");
        result.Should().NotContain("filing-summary.htm");
    }
}
