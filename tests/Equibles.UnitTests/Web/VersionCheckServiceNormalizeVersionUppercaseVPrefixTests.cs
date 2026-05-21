using System.Reflection;
using Equibles.Web.Services;
using Xunit;

namespace Equibles.UnitTests.Web;

/// <summary>
/// NormalizeVersion's docstring only mentions stripping a leading "v", but the
/// implementation accepts both "v" and "V". A release tagged "V1.2.3" must
/// normalize the same as "v1.2.3", otherwise IsNewer fails to parse it and the
/// update banner silently never appears.
/// </summary>
public class VersionCheckServiceNormalizeVersionUppercaseVPrefixTests
{
    [Fact]
    public void NormalizeVersion_UppercaseVPrefix_StripsPrefixSoVersionParses()
    {
        var method = typeof(VersionCheckService).GetMethod(
            "NormalizeVersion",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var normalized = (string)method.Invoke(null, ["V1.2.3"]);

        Version.TryParse(normalized, out var parsed).Should().BeTrue();
        parsed.Should().Be(new Version(1, 2, 3));
    }
}
