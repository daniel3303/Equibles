using System.Reflection;
using Equibles.Web.Services;

namespace Equibles.UnitTests.Web;

/// <summary>
/// IsNewer's documented contract names a specific scenario verbatim: "Compares
/// Major.Minor.Build only so tag formatting variance (e.g. 'v1.0.0' vs an
/// assembly '1.0.0.0') can't trigger a spurious banner." The running portal's
/// current version comes from the 4-segment assembly version ("1.0.0.0");
/// GitHub tags are 3-segment ("1.0.0"). The existing IsNewer test only pins the
/// 2-vs-3-segment clamp — the exact 4-segment-assembly-vs-3-segment-tag case the
/// contract calls out is unpinned. A refactor of ToCore that stopped discarding
/// the Revision segment would make the same release compare as newer and show a
/// permanent, un-dismissable "update available" banner to every operator.
/// </summary>
public class VersionCheckServiceIsNewerAssemblyVsTagTests
{
    [Fact]
    public void IsNewer_FourSegmentAssemblyVsThreeSegmentTag_NoSpuriousBannerButRealBumpDetected()
    {
        var method = typeof(VersionCheckService).GetMethod(
            "IsNewer",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        // Same release, only the assembly's 4th (Revision) segment differs:
        // the contract forbids this from reporting an update.
        var sameRelease = (bool)method.Invoke(null, ["1.0.0.0", "1.0.0"]);
        // Control: a genuine Minor bump must still be detected even when the
        // current side carries the extra assembly Revision segment.
        var realBump = (bool)method.Invoke(null, ["1.0.0.0", "1.1.0"]);

        sameRelease
            .Should()
            .BeFalse("'1.0.0.0' and '1.0.0' are the same Major.Minor.Build release");
        realBump.Should().BeTrue("'1.1.0' is a higher Minor than '1.0.0.0' and is a real update");
    }
}
