using Equibles.Congress.HostedService.Services;

namespace Equibles.UnitTests.Congress;

/// <summary>
/// Pins the single-pass composition of <see cref="DisclosureParsingHelper.NormalizeMemberName"/>'s
/// two transforms. When a filing injects an honorific BETWEEN a doubled first name
/// ("Scott Mr Scott Franklin"), dropping the honorific must leave the two "Scott"
/// tokens adjacent so the repeated-token collapse still fires in the same pass —
/// yielding the canonical "Scott Franklin", not "Scott Scott Franklin". The existing
/// theory covers each transform alone but not their interaction (#3374).
/// </summary>
public class DisclosureParsingHelperNormalizeMemberNameHonorificBetweenDoubledTokensTests
{
    [Fact]
    public void NormalizeMemberName_HonorificBetweenDoubledFirstName_CollapsesToCanonical()
    {
        DisclosureParsingHelper
            .NormalizeMemberName("Scott Mr Scott Franklin")
            .Should()
            .Be("Scott Franklin");
    }
}
