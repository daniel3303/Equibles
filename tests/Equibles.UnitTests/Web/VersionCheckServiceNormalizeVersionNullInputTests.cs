using System.Reflection;
using Equibles.Web.Services;

namespace Equibles.UnitTests.Web;

/// <summary>
/// Pins NormalizeVersion's null/empty pass-through. Sibling to the existing
/// SemVer-pre-release pin. The method is the input adapter for IsNewer; a
/// regression that dropped the IsNullOrEmpty guard would NRE on `value.Trim()`
/// whenever the assembly ships without an InformationalVersion attribute and
/// `GetCurrentVersion` falls back to a null literal — crashing the update
/// banner check on the very first request after a release built without
/// SourceLink.
/// </summary>
public class VersionCheckServiceNormalizeVersionNullInputTests
{
    [Fact]
    public void NormalizeVersion_NullInput_ReturnsNullWithoutThrowing()
    {
        var method = typeof(VersionCheckService).GetMethod(
            "NormalizeVersion",
            BindingFlags.NonPublic | BindingFlags.Static
        )!;

        var result = method.Invoke(null, [null]);

        result.Should().BeNull();
    }
}
