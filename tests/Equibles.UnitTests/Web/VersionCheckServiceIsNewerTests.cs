using System.Reflection;
using Equibles.Web.Services;

namespace Equibles.UnitTests.Web;

/// <summary>
/// IsNewer's documented contract: "Compares Major.Minor.Build only so tag
/// formatting variance (e.g. 'v1.0.0' vs an assembly '1.0.0.0') can't trigger a
/// spurious banner." A two-segment version ("1.2") parses with Build == -1; the
/// contract requires that to be treated as Build 0, so "1.2" vs "1.2.0" is the
/// same release and must NOT report an update. The existing integration test
/// only covers four-segment latest tags (Build >= 0); the missing-Build clamp is
/// unpinned. A refactor dropping `Build &lt; 0 ? 0 : Build` would make the
/// System.Version ctor throw on a negative build — surfacing as either an
/// exception or a spurious banner, the exact failure the contract forbids.
/// </summary>
public class VersionCheckServiceIsNewerTests
{
    [Fact]
    public void IsNewer_TwoSegmentCurrentVsThreeSegmentLatest_TreatsMissingBuildAsNoUpdate()
    {
        var method = typeof(VersionCheckService).GetMethod(
            "IsNewer",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var result = (bool)method.Invoke(null, ["1.2", "1.2.0"]);

        result.Should().BeFalse("'1.2' and '1.2.0' are the same Major.Minor.Build release");
    }
}
