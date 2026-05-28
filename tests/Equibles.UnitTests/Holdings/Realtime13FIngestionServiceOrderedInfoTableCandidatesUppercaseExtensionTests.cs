using System.Reflection;
using Equibles.Holdings.HostedService.Services;

namespace Equibles.UnitTests.Holdings;

public class Realtime13FIngestionServiceOrderedInfoTableCandidatesUppercaseExtensionTests
{
    // Adversarial Lane A. The XML doc on OrderedInfoTableCandidates calls
    // out SEC's inconsistent naming (`infotable.xml`,
    // `form13fInfoTable.xml`, `<accession>.xml`) — i.e. case varies. The
    // existing PrimaryDocUppercase test pins case-insensitive EXCLUSION of
    // the cover page, but a regression of the `EndsWith(".xml", IgnoreCase)`
    // guard to plain `Ordinal` would still pass that test (uppercase
    // `PRIMARY_DOC.XML` would be filtered out for the wrong reason — failing
    // the EndsWith). It would silently DROP every non-primary uppercase
    // `.XML` artifact and the caller would get an empty candidate list →
    // realtime ingestion stops finding info tables on filings that ship
    // uppercase extensions.
    [Fact]
    public void OrderedInfoTableCandidates_UppercaseXmlExtensionOnInfoTable_StillIncludedAndYieldedFirst()
    {
        List<string> artifacts = ["rendering.XML", "form13fInfoTable.XML"];

        var method = typeof(Realtime13FIngestionService).GetMethod(
            "OrderedInfoTableCandidates",
            BindingFlags.NonPublic | BindingFlags.Static
        );
        var result = ((IEnumerable<string>)method.Invoke(null, [artifacts])).ToList();

        // Both .XML files must survive the extension filter, and the
        // table-looking name must still be yielded first per the doc.
        result.Should().Equal("form13fInfoTable.XML", "rendering.XML");
    }
}
