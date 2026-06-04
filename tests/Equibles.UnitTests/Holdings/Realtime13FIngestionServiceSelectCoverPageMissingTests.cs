using System.Reflection;
using Equibles.Holdings.HostedService.Services;

namespace Equibles.UnitTests.Holdings;

public class Realtime13FIngestionServiceSelectCoverPageMissingTests
{
    // SelectCoverPage locates the filing's primary_doc.xml among its artifacts. When none is
    // present it must return null so the caller skips the filing ("no primary_doc.xml, skipping")
    // rather than mis-picking a non-cover artifact. The existing test only covers the match case;
    // this pins the no-match → null branch. A fallback-to-first regression would break it.
    [Fact]
    public void SelectCoverPage_NoPrimaryDocArtifact_ReturnsNull()
    {
        List<string> artifacts = ["form13fInfoTable.xml", "primary_doc.xsd"];

        var method = typeof(Realtime13FIngestionService).GetMethod(
            "SelectCoverPage",
            BindingFlags.NonPublic | BindingFlags.Static
        );
        var result = (string)method.Invoke(null, [artifacts]);

        result.Should().BeNull();
    }
}
