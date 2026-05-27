using System.Reflection;
using Equibles.Web.Services;

namespace Equibles.UnitTests.Web;

public class VersionCheckServiceIsNewerUnparseableLatestTests
{
    [Fact]
    public void IsNewer_UnparseableLatestVersion_ReturnsFalseFailingClosed()
    {
        // Sibling to IsNewerUnparseable, which pins the FIRST arm of the
        // OR (`!Version.TryParse(NormalizeVersion(current), …)`). The
        // SECOND arm — `!Version.TryParse(latest, …)` — is unpinned. A
        // refactor that drops the latest TryParse and uses Version.Parse
        // directly (or that only guards the current side under "the
        // GitHub release tag is always a valid version") would compile,
        // pass the existing pin, and throw FormatException up the request
        // path the first time GitHub returns a malformed `tag_name`
        // (release notes editors occasionally hand-edit tags into
        // non-version strings like "v1.0-beta-readme-update"). Pin the
        // fail-closed contract on the latest side.
        var method = typeof(VersionCheckService).GetMethod(
            "IsNewer",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var result = (bool)method!.Invoke(null, ["1.2.3", "not a version"]);

        result.Should().BeFalse();
    }
}
