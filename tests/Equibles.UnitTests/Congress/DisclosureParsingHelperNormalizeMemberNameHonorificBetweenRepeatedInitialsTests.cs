using Equibles.Congress.HostedService.Services;

namespace Equibles.UnitTests.Congress;

/// <summary>
/// Adversarial seam between two documented guarantees of
/// <see cref="DisclosureParsingHelper.NormalizeMemberName"/>: it drops honorific
/// tokens in any position (GH-3374) yet must keep a genuine repeated initial
/// intact to avoid merging distinct people (GH-3989). Dropping an honorific that
/// sits BETWEEN two same-letter initials ("C. Mr C. Franklin") makes the two
/// "C." tokens adjacent in the running result — the exact shape the doubled-token
/// collapse targets — so the initial exemption must still win and preserve both.
/// </summary>
public class DisclosureParsingHelperNormalizeMemberNameHonorificBetweenRepeatedInitialsTests
{
    [Fact]
    public void NormalizeMemberName_HonorificBetweenRepeatedInitials_KeepsBothInitials()
    {
        DisclosureParsingHelper
            .NormalizeMemberName("C. Mr C. Franklin")
            .Should()
            .Be("C. C. Franklin");
    }
}
