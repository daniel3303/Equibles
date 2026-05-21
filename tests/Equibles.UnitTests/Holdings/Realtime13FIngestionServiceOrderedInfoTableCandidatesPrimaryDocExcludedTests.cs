using System.Reflection;
using Equibles.Holdings.HostedService.Services;

namespace Equibles.UnitTests.Holdings;

public class Realtime13FIngestionServiceOrderedInfoTableCandidatesPrimaryDocExcludedTests
{
    // OrderedInfoTableCandidates' doc-comment is explicit: "The information
    // table is one of the filing's NON-COVER .xml artifacts." primary_doc.xml
    // is the cover page (selected separately by SelectCoverPage) and must
    // never appear in the info-table candidate list — otherwise the real-time
    // ingestor would try to parse the cover sheet as a list of holdings and
    // either NRE on the missing infoTable element or, worse, silently
    // misinterpret the manager-identity block as positions. The sibling
    // TableLikeNameAfterUnrelated pin covers the ordering arm; this pin
    // covers the cover-exclusion filter — a refactor that dropped the
    // `!a.Equals("primary_doc.xml")` predicate would compile, pass the
    // ordering test (primary_doc doesn't contain info/table/13f so it sorts
    // last) and silently leak the cover page back into the candidate stream.
    [Fact]
    public void OrderedInfoTableCandidates_PrimaryDocXmlAlongsideInfoTable_PrimaryDocExcluded()
    {
        List<string> artifacts = ["primary_doc.xml", "form13fInfoTable.xml"];

        var method = typeof(Realtime13FIngestionService).GetMethod(
            "OrderedInfoTableCandidates",
            BindingFlags.NonPublic | BindingFlags.Static
        );
        var result = ((IEnumerable<string>)method.Invoke(null, [artifacts])).ToList();

        result.Should().Equal("form13fInfoTable.xml");
        result.Should().NotContain("primary_doc.xml");
    }
}
