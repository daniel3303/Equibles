using Equibles.Holdings.Data.Models;
using Equibles.Holdings.HostedService.Services;

namespace Equibles.UnitTests.Holdings;

public class HoldingsParsingHelperParseInvestmentDiscretionNullTests
{
    [Fact]
    public void ParseInvestmentDiscretion_NullInput_ReturnsSoleWithoutThrowing()
    {
        // ParseInvestmentDiscretion's pipeline (HoldingsParsingHelper.cs:110)
        // is `value?.ToUpperInvariant() switch { … _ => Sole }`. The `?.`
        // null-conditional turns a null input into a null pattern that
        // matches the default arm — yielding Sole. Existing pins cover
        // SOLE / DFND / OTR / a non-null unknown value. A refactor that
        // "tightens" the pipeline to `value.ToUpperInvariant()` (under the
        // assumption "the caller never passes null") would NRE on the first
        // 13F row whose Discretion cell is genuinely absent — taking down
        // the whole quarterly import. Pin the null-tolerance contract: the
        // value `null` must return Sole, not throw.
        var result = HoldingsParsingHelper.ParseInvestmentDiscretion(null);

        result.Should().Be(InvestmentDiscretion.Sole);
    }
}
