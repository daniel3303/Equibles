using System.Reflection;
using Equibles.Holdings.Repositories;

namespace Equibles.UnitTests.Holdings;

/// <summary>
/// Adversarial Lane A. <c>EscapeLikePattern</c> guards <c>SearchNameOrCik</c>
/// against LIKE-injection: a user typing "50%" must match the literal three
/// characters, never every name. The order of the three <c>.Replace</c> calls
/// is load-bearing: backslash MUST be escaped FIRST, so the backslashes
/// synthesised by the subsequent <c>%</c> / <c>_</c> escapes don't get
/// double-escaped on a second pass. A refactor that reorders the chain
/// (or uses a regex with the wrong alternation order) would silently emit
/// <c>\\\%</c> instead of <c>\%</c> — the LIKE engine then sees an escaped
/// backslash followed by a literal <c>%</c> wildcard, and the typeahead
/// returns every row again.
/// </summary>
public class InstitutionalHolderRepositoryEscapeLikePatternTests
{
    [Fact]
    public void EscapeLikePattern_MixedBackslashPercentUnderscore_EscapesEachExactlyOnce()
    {
        // Input mixes all three LIKE specials so the test exercises the
        // ordering invariant: input is "a\b%c_d", expected "a\\b\%c\_d".
        const string input = "a\\b%c_d";

        var method = typeof(InstitutionalHolderRepository).GetMethod(
            "EscapeLikePattern",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var result = (string)method.Invoke(null, [input]);

        result
            .Should()
            .Be(
                "a\\\\b\\%c\\_d",
                "reordering the Replace chain would double-escape the backslashes synthesised by the % and _ escapes"
            );
    }
}
