using Equibles.Congress.HostedService.Services;

namespace Equibles.UnitTests.Congress;

/// <summary>
/// Adversarial: <see cref="DisclosureParsingHelper.NormalizeMemberName"/> documents
/// that it keeps genuine initials untouched and deliberately does NOT reconcile
/// initial/order variants because doing so "risks merging two distinct people"
/// (no over-merging). A repeated identical initial — e.g. a member filed with two
/// same-letter leading initials ("C. C. Franklin") — is a genuine initial sequence,
/// so by that contract it must pass through unchanged. The doubled-token collapse
/// (intended for the parser's doubled *first name*, "Scott Scott") must not fold a
/// real repeated initial into a single one and silently merge a distinct identity.
/// </summary>
public class DisclosureParsingHelperNormalizeMemberNameRepeatedInitialTests
{
    [Fact(
        Skip = "GH-3989 — doubled-token collapse folds a genuine repeated initial: \"C. C. Franklin\" → \"C. Franklin\""
    )]
    public void NormalizeMemberName_RepeatedGenuineInitial_LeavesBothInitialsIntact()
    {
        DisclosureParsingHelper.NormalizeMemberName("C. C. Franklin").Should().Be("C. C. Franklin");
    }
}
