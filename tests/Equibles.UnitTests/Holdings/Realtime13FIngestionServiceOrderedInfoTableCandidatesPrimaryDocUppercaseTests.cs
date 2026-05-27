using System.Reflection;
using Equibles.Holdings.HostedService.Services;

namespace Equibles.UnitTests.Holdings;

/// <summary>
/// Sibling to <c>OrderedInfoTableCandidatesPrimaryDocExcludedTests</c>. The
/// existing pin uses lowercase <c>primary_doc.xml</c>, so a refactor that drops
/// the <c>StringComparison.OrdinalIgnoreCase</c> argument from the exclusion's
/// <c>Equals</c> call would silently still pass that test (lowercase equals
/// lowercase with either comparer). The case-insensitivity is its own
/// load-bearing contract though — SEC EDGAR sometimes serves uppercase artifact
/// names (older AT&amp;T-era submissions, EDGAR-online re-processing), and
/// without the IgnoreCase flag a <c>PRIMARY_DOC.XML</c> cover page would leak
/// into the info-table candidate stream and be parsed as a holdings table.
/// </summary>
public class Realtime13FIngestionServiceOrderedInfoTableCandidatesPrimaryDocUppercaseTests
{
    [Fact]
    public void OrderedInfoTableCandidates_UppercasePrimaryDocAlongsideInfoTable_PrimaryDocStillExcluded()
    {
        List<string> artifacts = ["PRIMARY_DOC.XML", "form13fInfoTable.xml"];

        var method = typeof(Realtime13FIngestionService).GetMethod(
            "OrderedInfoTableCandidates",
            BindingFlags.NonPublic | BindingFlags.Static
        );
        var result = ((IEnumerable<string>)method.Invoke(null, [artifacts])).ToList();

        result.Should().Equal("form13fInfoTable.xml");
        result.Should().NotContain("PRIMARY_DOC.XML");
    }
}
