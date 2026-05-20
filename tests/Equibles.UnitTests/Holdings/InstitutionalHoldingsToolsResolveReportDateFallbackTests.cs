using System.Reflection;
using Equibles.Holdings.Mcp.Tools;

namespace Equibles.UnitTests.Holdings;

/// <summary>
/// Pins the fallback contract of ResolveReportDate — the helper just extracted
/// in #1301 to collapse four duplicated parse-and-validate ternaries across
/// the holdings MCP tool. When the input string is malformed or doesn't match
/// any of the holder's report dates, the helper returns validDates[0]. Every
/// caller passes a list ordered OrderByDescending(d => d), so [0] is the most
/// recent date — the established "default to the current quarter" semantics.
/// A regression returning validDates[^1] (the oldest) would silently swap
/// every defaulting MCP call to point at the oldest report instead.
/// </summary>
public class InstitutionalHoldingsToolsResolveReportDateFallbackTests
{
    [Fact]
    public void ResolveReportDate_InputNotInValidDates_FallsBackToMostRecentFirstEntry()
    {
        var method = typeof(InstitutionalHoldingsTools).GetMethod(
            "ResolveReportDate",
            BindingFlags.NonPublic | BindingFlags.Static
        )!;

        IReadOnlyList<DateOnly> validDates =
        [
            new DateOnly(2024, 9, 30),
            new DateOnly(2024, 6, 30),
            new DateOnly(2024, 3, 31),
        ];

        var result = (DateOnly)method.Invoke(null, ["not-a-date", validDates])!;

        result.Should().Be(new DateOnly(2024, 9, 30));
    }
}
