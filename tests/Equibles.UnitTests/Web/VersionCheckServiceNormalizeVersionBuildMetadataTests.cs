using System.Reflection;
using Equibles.Web.Services;

namespace Equibles.UnitTests.Web;

/// <summary>
/// Sibling to VersionCheckServiceNormalizeVersionTests' pre-release pin. That
/// pin covers the SemVer "-" pre-release arm. The "+" build-metadata arm is
/// independently load-bearing — `IndexOfAny(['-', '+'])` cuts at whichever
/// separator appears first. A refactor that simplified the cut to
/// `IndexOf('-')` (intuitive: "strip the pre-release suffix") would compile,
/// pass the existing pre-release pin, and silently fail to strip the build
/// metadata; `Version.TryParse("1.0.0+build.123")` returns false, IsNewer
/// returns false, and the update banner never appears for any release tag
/// carrying build metadata.
/// </summary>
public class VersionCheckServiceNormalizeVersionBuildMetadataTests
{
    [Fact]
    public void NormalizeVersion_SemVerBuildMetadataTag_ProducesAVersionParseableCoreVersion()
    {
        // "v2.1.0+build.123" exercises both the "v" prefix strip and the
        // "+" build-metadata cut. The contract is "so the value parses with
        // System.Version" — assert the promise, not the literal string.
        var method = typeof(VersionCheckService).GetMethod(
            "NormalizeVersion",
            BindingFlags.NonPublic | BindingFlags.Static
        )!;

        var normalized = (string)method.Invoke(null, ["v2.1.0+build.123"])!;

        Version.TryParse(normalized, out var parsed).Should().BeTrue();
        parsed.Should().Be(new Version(2, 1, 0));
    }
}
