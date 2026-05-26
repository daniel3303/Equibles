using System.Reflection;
using Equibles.Sec.HostedService.Services;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// Callers pass XElement.Value directly to ParseBool without trimming.
/// Malformed SEC filings can have insignificant whitespace around the
/// boolean text (e.g. "&lt;isDirector&gt; 1 &lt;/isDirector&gt;"), and
/// XElement.Value preserves it. The current pattern-match requires an
/// exact string match, so whitespace-padded truthy values silently
/// return false — dropping isDirector/isOfficer/isTenPercentOwner flags.
/// </summary>
public class InsiderTradingFilingProcessorParseBoolWhitespacePaddedTests
{
    private static readonly MethodInfo ParseBoolMethod =
        typeof(InsiderTradingFilingProcessor).GetMethod(
            "ParseBool",
            BindingFlags.NonPublic | BindingFlags.Static
        );

    private static bool ParseBool(string value) => (bool)ParseBoolMethod.Invoke(null, [value]);

    [Fact(Skip = "GH-2106 — ParseBool exact-match drops whitespace-padded truthy values")]
    public void ParseBool_WhitespacePaddedOne_ReturnsTrue()
    {
        var result = ParseBool(" 1 ");

        result.Should().BeTrue();
    }
}
