using System.Reflection;
using Equibles.Congress.HostedService.Services;

namespace Equibles.UnitTests.Congress;

/// <summary>
/// Pins the newly-extracted private static <c>StripHonorificPrefixes</c>
/// helper (#1422). The method name's plural — <i>"Prefixe<b>s</b>"</i> — and
/// the loop body's iteration over every entry in the <c>HonorificPrefixes</c>
/// array advertise the same contract: every recognised honorific in the input
/// must be removed, not just the first one matched.
///
/// House XML occasionally renders a name with stacked honorifics (e.g. when a
/// clerk types "Mr. John" into the <c>&lt;First&gt;</c> field while the
/// <c>&lt;Prefix&gt;</c> element already carries "Hon."), so the assembled
/// string is <c>"Hon. Mr. Smith"</c>. The dedupe-by-name path downstream would
/// otherwise treat the stacked variant and the canonical "Smith" as distinct
/// members. A naïve refactor to <c>StartsWith</c>+<c>Substring</c> that
/// short-circuited after the first hit would silently regress this.
/// </summary>
public class HouseDisclosureClientStripHonorificPrefixesStackedTests
{
    private static readonly MethodInfo StripHonorificPrefixesMethod =
        typeof(HouseDisclosureClient).GetMethod(
            "StripHonorificPrefixes",
            BindingFlags.NonPublic | BindingFlags.Static
        );

    [Fact]
    public void StripHonorificPrefixes_StackedHonAndMr_RemovesBothPrefixes()
    {
        var result = (string)StripHonorificPrefixesMethod.Invoke(null, ["Hon. Mr. Smith"]);

        result.Should().Be("Smith");
    }
}
