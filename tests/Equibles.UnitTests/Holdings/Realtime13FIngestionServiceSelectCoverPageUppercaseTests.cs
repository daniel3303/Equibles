using System.Reflection;
using Equibles.Holdings.HostedService.Services;

namespace Equibles.UnitTests.Holdings;

public class Realtime13FIngestionServiceSelectCoverPageUppercaseTests
{
    // SelectCoverPage uses `Equals(..., StringComparison.OrdinalIgnoreCase)`,
    // not a culture-sensitive or strict comparison. SEC EDGAR sometimes serves
    // uppercase artifact names (older AT&T-era submissions, scanned filings
    // re-processed by EDGAR-online), so the cover-page lookup must match
    // "PRIMARY_DOC.XML" identically to "primary_doc.xml".
    //
    // The sibling OrderedInfoTableCandidatesPrimaryDocExcluded pin tests the
    // exclusion filter on the lowercase canonical name; this pin tests the
    // case-insensitive match arm directly. A refactor that switched to a
    // strict comparison (or, more subtly, to a Turkish-locale-sensitive
    // upper-cased comparison) would silently drop the cover page on those
    // filings and the whole 13F-HR would be skipped as "no primary_doc.xml".
    [Fact]
    public void SelectCoverPage_ArtifactNamedAllCaps_MatchesCaseInsensitively()
    {
        List<string> artifacts = ["PRIMARY_DOC.XML", "form13fInfoTable.xml"];

        var method = typeof(Realtime13FIngestionService).GetMethod(
            "SelectCoverPage",
            BindingFlags.NonPublic | BindingFlags.Static
        );
        var result = (string)method.Invoke(null, [artifacts]);

        result.Should().Be("PRIMARY_DOC.XML");
    }
}
