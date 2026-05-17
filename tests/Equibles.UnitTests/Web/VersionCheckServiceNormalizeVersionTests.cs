using System.Reflection;
using Equibles.Web.Services;
using Xunit;

namespace Equibles.UnitTests.Web;

/// <summary>
/// NormalizeVersion's documented contract: "Strips a leading 'v' and any
/// pre-release/build metadata so the value parses with System.Version."
/// GitHub release tags are routinely SemVer pre-releases (e.g. "v2.1.0-rc.1");
/// if the metadata isn't cut, System.Version.TryParse fails and IsNewer
/// silently returns false — the update banner would never appear for any
/// release cut from a pre-release tag. Existing tests only feed plain tags.
/// </summary>
public class VersionCheckServiceNormalizeVersionTests
{
    [Fact]
    public void NormalizeVersion_SemVerPreReleaseTag_ProducesAVersionParseableCoreVersion()
    {
        var method = typeof(VersionCheckService).GetMethod(
            "NormalizeVersion",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var normalized = (string)method.Invoke(null, ["v2.1.0-rc.1"]);

        // The contract is "so the value parses with System.Version" — assert the
        // promise, not the literal string.
        Version.TryParse(normalized, out var parsed).Should().BeTrue();
        parsed.Should().Be(new Version(2, 1, 0));
    }
}
