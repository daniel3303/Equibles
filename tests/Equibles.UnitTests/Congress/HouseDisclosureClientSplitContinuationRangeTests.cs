using System.Reflection;
using Equibles.Congress.HostedService.Services;

namespace Equibles.UnitTests.Congress;

public class HouseDisclosureClientSplitContinuationRangeTests
{
    // SplitContinuation's contract: the amount is "always the trailing $… run",
    // so a wrapped amount range carrying two '$' (e.g. "$1,000,001 - $5,000,000")
    // must be returned WHOLE as the amount — splitting at the last '$' instead of
    // the first would corrupt both the asset text and the amount range.
    [Fact]
    public void SplitContinuation_AssetThenAmountRangeWithTwoDollars_KeepsWholeRangeAsAmount()
    {
        var method = typeof(HouseDisclosureClient).GetMethod(
            "SplitContinuation",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var result = method!.Invoke(null, ["Acme Corp Bonds $1,000,001 - $5,000,000"]);
        var (asset, amount) = ((string, string))result;

        asset.Should().Be("Acme Corp Bonds");
        amount.Should().Be("$1,000,001 - $5,000,000");
    }
}
