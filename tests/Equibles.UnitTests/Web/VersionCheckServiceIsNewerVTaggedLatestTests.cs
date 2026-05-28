using System.Reflection;
using Equibles.Web.Services;

namespace Equibles.UnitTests.Web;

/// <summary>
/// Adversarial Lane A. IsNewer's documented contract calls out tag formatting
/// variance verbatim: "Compares Major.Minor.Build only so tag formatting
/// variance (e.g. 'v1.0.0' vs an assembly '1.0.0.0') can't trigger a spurious
/// banner." Git tags conventionally carry a leading 'v' (the same docstring
/// gives "v1.0.0" as the example), so a real GitHub release tag fed in as the
/// 'latest' argument will be 'v&lt;Major&gt;.&lt;Minor&gt;.&lt;Build&gt;'.
///
/// NormalizeVersion is the helper that strips the 'v' prefix — and the same
/// file's GetCurrentVersion already routes the assembly's
/// InformationalVersion through it. The contract demands the same treatment
/// for the comparison input, otherwise a real-world tag like 'v1.1.0' would
/// fail Version.TryParse, IsNewer would short-circuit to false, and operators
/// would never see the banner for a legitimate release.
///
/// Pin: a bare current ('1.0.0') against a v-tagged latest ('v1.1.0') is a
/// real bump and must return true. Bug class established by the existing
/// AssemblyVsTag and Unparseable sibling tests — both pin tag-format
/// tolerance on one side of the comparison; this pins it on the other.
/// </summary>
public class VersionCheckServiceIsNewerVTaggedLatestTests
{
    [Fact]
    public void IsNewer_BareCurrentVsVPrefixedLatest_DetectsBumpDespiteTagPrefix()
    {
        var method = typeof(VersionCheckService).GetMethod(
            "IsNewer",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var result = (bool)method.Invoke(null, ["1.0.0", "v1.1.0"]);

        result
            .Should()
            .BeTrue(
                "'v1.1.0' is the conventional GitHub release-tag format the docstring names verbatim; a real release tag must not be silently dropped as unparseable, otherwise the in-app update banner never fires for v-prefixed tags"
            );
    }
}
