using System.Reflection;
using Equibles.Web.Services;

namespace Equibles.UnitTests.Web;

public class VersionCheckServiceIsNewerBuildBumpTests
{
    [Fact]
    public void IsNewer_BuildSegmentBumpOnly_DetectsAsUpdate()
    {
        // Existing pins cover the failure arms (unparseable current/latest)
        // and the Minor bump path (AssemblyVsTag). The Build (patch) bump
        // path is unpinned — the dominant cadence of a maintenance release
        // (e.g. 1.0.0 → 1.0.1) is not exercised by any pin. A refactor that
        // narrowed ToCore to `new(Major, Minor, 0)` (e.g. "tags only track
        // Major.Minor") would compile, pass AssemblyVsTag (the Minor bump
        // still beats the prior Minor), and silently swallow every Build
        // bump — operators would never see a banner for patch releases.
        // The contract: same Major.Minor with a higher Build is an update.
        var method = typeof(VersionCheckService).GetMethod(
            "IsNewer",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var result = (bool)method!.Invoke(null, ["1.0.0", "1.0.1"]);

        result.Should().BeTrue();
    }
}
