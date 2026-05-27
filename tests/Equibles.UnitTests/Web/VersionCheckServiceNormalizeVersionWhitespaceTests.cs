using System.Reflection;
using Equibles.Web.Services;

namespace Equibles.UnitTests.Web;

public class VersionCheckServiceNormalizeVersionWhitespaceTests
{
    // Sibling to the existing NormalizeVersion pins (SemVerPreReleaseTag,
    // SemVerBuildMetadataTag, NullInput, UppercaseVPrefix). None of them
    // exercises the leading `value.Trim()` step at the top of the method.
    //
    // The Trim step is load-bearing in production: an
    // `AssemblyInformationalVersionAttribute` can legitimately carry
    // padding when a CI build script emits the version literal via env
    // var interpolation with a stray newline or trailing space
    // (`-p:InformationalVersion="${{ secrets.VERSION }}"` where the
    // secret was set with a trailing newline — a common gotcha in
    // GitHub Actions). Without Trim, `" v1.2.3 ".StartsWith('v')` is
    // FALSE (starts with space), the v-prefix strip silently no-ops,
    // and `Version.TryParse(" v1.2.3 ")` returns false — `IsNewer`
    // then returns false for every version comparison and the update
    // banner is permanently hidden with no log signal.
    //
    // The risks this pin uniquely catches:
    //   • Drop Trim — a "simplify" cleanup that inlines the v-prefix
    //     strip directly on the raw value. Pin assertion is the
    //     `Version.TryParse` promise that the existing siblings also
    //     use, so the regression mode is consistent across the family.
    //   • Reorder steps so Trim happens AFTER v-strip — the v-prefix
    //     check would no-op on padded input (same failure mode).
    //
    // Pin: feed a tag with both leading AND trailing whitespace, the
    // canonical pre-release v-prefix shape; assert the result parses
    // to the expected core Version. The whitespace must be on BOTH
    // sides to catch a `TrimEnd()`-only or `TrimStart()`-only
    // narrowing regression.
    [Fact]
    public void NormalizeVersion_LeadingAndTrailingWhitespace_ProducesAVersionParseableCoreVersion()
    {
        var method = typeof(VersionCheckService).GetMethod(
            "NormalizeVersion",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var normalized = (string)method.Invoke(null, ["  v1.2.3  "]);

        Version.TryParse(normalized, out var parsed).Should().BeTrue();
        parsed.Should().Be(new Version(1, 2, 3));
    }
}
