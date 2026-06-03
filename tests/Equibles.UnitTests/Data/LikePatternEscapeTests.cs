using Equibles.Data;

namespace Equibles.UnitTests.Data;

public class LikePatternEscapeTests
{
    // Adversarial Lane A. Contract: Escape makes '\' '%' '_' match literally under ILIKE.
    // The order is load-bearing — '\' MUST be escaped before '%'/'_', or the backslash that
    // escaping '_' introduces ("\_") gets doubled into "\\_" (a literal backslash + a live
    // '_' wildcard), leaking matches. The existing Contains pin only feeds '%', so neither
    // the backslash rule nor its ordering is covered. Input mixes both: a\b_c must come back
    // with the backslash doubled and the underscore escaped exactly once.
    [Fact]
    public void Escape_BackslashAndUnderscore_DoublesBackslashAndEscapesUnderscoreInOrder()
    {
        var result = LikePattern.Escape(@"a\b_c");

        result.Should().Be(@"a\\b\_c");
    }
}
