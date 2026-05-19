using Equibles.Sec.FinancialFacts.Mcp.Helpers;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// FactMarkdown.Clean's doc-comment says "Strips control chars from untrusted
/// args before they reach logs / the error store". The canonical log-injection
/// attack is an attacker injecting <c>\r\n</c> into a user-supplied value to
/// forge a new log entry. CR (U+000D) and LF (U+000A) are both C0 control
/// chars, so a refactor that swaps <c>char.IsControl</c> for a narrower
/// whitelist (e.g. only stripping ASCII C0 below space, or only filtering
/// non-printable categories) must still kill both — otherwise log entries
/// become attacker-controlled.
/// </summary>
public class FactMarkdownCleanLogInjectionTests
{
    [Fact]
    public void Clean_CarriageReturnAndLineFeed_AreStrippedFromValue()
    {
        var result = FactMarkdown.Clean("alert\r\nbadhost");

        result
            .Should()
            .Be(
                "alertbadhost",
                "CR and LF are control chars and must be stripped to prevent log-injection forgery"
            );
    }
}
