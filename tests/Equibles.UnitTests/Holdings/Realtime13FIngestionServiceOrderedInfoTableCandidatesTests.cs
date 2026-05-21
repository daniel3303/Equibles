using System.Reflection;
using Equibles.Holdings.HostedService.Services;

namespace Equibles.UnitTests.Holdings;

public class Realtime13FIngestionServiceOrderedInfoTableCandidatesTests
{
    // OrderedInfoTableCandidates documents: "Yield the table-looking names
    // first, then the rest." The caller validates by content rather than
    // trusting a single name guess, so the helper's ONLY job is to put info /
    // table / 13f-bearing names ahead of unrelated XMLs. A flipped
    // OrderByDescending → OrderBy on the bool keyname would silently emit
    // rendering / schema XMLs first, sending the caller down the wrong
    // artifact and triggering parse failures on every real-time ingestion.
    [Fact]
    public void OrderedInfoTableCandidates_TableLikeNameAfterUnrelated_TableLikeYieldedFirst()
    {
        List<string> artifacts = ["rendering.xml", "formInfo13f.xml"];

        var method = typeof(Realtime13FIngestionService).GetMethod(
            "OrderedInfoTableCandidates",
            BindingFlags.NonPublic | BindingFlags.Static
        );
        var result = ((IEnumerable<string>)method.Invoke(null, [artifacts])).ToList();

        result.Should().Equal("formInfo13f.xml", "rendering.xml");
    }
}
