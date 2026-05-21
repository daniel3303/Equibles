using Equibles.Sec.FinancialFacts.Mcp.Helpers;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// FactMarkdown.Value routes the unit through three category checks
/// (per-share, USD prefix, bare-currency) before falling through to the
/// dimensionless / ratio format. All three checks short-circuit safely on
/// `unit == null` via `u != null && …`. The existing per-unit pins all
/// supply a real unit string; the null-unit fallback is untested. A
/// regression that null-coalesced the unit to "USD" or that dropped the
/// `u != null` guard on `isWholeMagnitude` would silently format ratio
/// values as `$1` (USD-prefixed and rounded) instead of the documented
/// dimensionless `1.2345`.
/// </summary>
public class FactMarkdownValueNullUnitTests
{
    [Fact]
    public void Value_NullUnit_FallsThroughToDimensionlessFormatWithoutDollarPrefix()
    {
        var result = FactMarkdown.Value(1234.5678m, null);

        result.Should().Be("1234.5678");
    }
}
