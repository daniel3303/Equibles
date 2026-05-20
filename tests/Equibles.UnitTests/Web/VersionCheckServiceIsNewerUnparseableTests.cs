using System.Reflection;
using Equibles.Web.Services;

namespace Equibles.UnitTests.Web;

/// <summary>
/// Pins IsNewer's fail-closed contract on unparseable input. The method is the
/// gate for the in-app update banner; if a garbage version string slips through
/// (a corrupted GitHub Release tag, an InformationalVersion polluted by a
/// build-tool quirk), the safe answer is "no update available" — never a
/// thrown exception, never a spurious banner. A regression that returned true
/// on parse failure would surface a false-positive banner; one that omitted
/// the TryParse guard would throw FormatException up the request path.
/// </summary>
public class VersionCheckServiceIsNewerUnparseableTests
{
    [Fact]
    public void IsNewer_UnparseableCurrentVersion_ReturnsFalseFailingClosed()
    {
        var method = typeof(VersionCheckService).GetMethod(
            "IsNewer",
            BindingFlags.NonPublic | BindingFlags.Static
        )!;

        var result = (bool)method.Invoke(null, ["not a version", "1.2.3"])!;

        result.Should().BeFalse();
    }
}
