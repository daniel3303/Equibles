using System.Reflection;
using Equibles.Holdings.HostedService.Services;

namespace Equibles.UnitTests.Holdings;

public class Realtime13FIngestionServiceOrderedInfoTableCandidatesEmptyTests
{
    // The information table is one of the filing's NON-cover .xml artifacts. A filing carrying only
    // its cover doc (primary_doc.xml) plus a non-.xml file has no info-table candidate, so the
    // result must be empty. The existing tests check exclusion WITH a real table present; this pins
    // the all-excluded boundary so a regression admitting primary_doc or non-xml is caught.
    [Fact]
    public void OrderedInfoTableCandidates_OnlyCoverDocAndNonXml_ReturnsEmpty()
    {
        List<string> artifacts = ["primary_doc.xml", "primary_doc.html"];

        var method = typeof(Realtime13FIngestionService).GetMethod(
            "OrderedInfoTableCandidates",
            BindingFlags.NonPublic | BindingFlags.Static
        );
        var result = (IEnumerable<string>)method.Invoke(null, [artifacts]);

        result.Should().BeEmpty();
    }
}
