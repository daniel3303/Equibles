using System.Reflection;
using Equibles.Congress.HostedService.Services;

namespace Equibles.UnitTests.Congress;

public class DisclosureParsingHelperIsTransactionTableMissingDateTests
{
    [Fact]
    public void IsTransactionTable_AssetColumnOnlyNoDate_ReturnsFalse()
    {
        // Sibling to IsTransactionTableMissingAsset. Both `hasDate` and
        // `hasAsset` must be true (`hasDate && hasAsset`). Existing pin
        // covers `hasDate=true, hasAsset=false → false`; this isolates
        // the inverse `hasDate=false, hasAsset=true → false`. A refactor
        // that loosens the gate to a single condition (e.g. only checking
        // hasAsset because "the date is always there") would compile,
        // pass the MissingAsset pin, and start picking up MEMBER-LIST or
        // ASSET-PORTFOLIO summary tables on the disclosure page — every
        // such "table" would feed empty rows downstream. Pin date-only
        // absence: asset-only headers must NOT qualify.
        var method = typeof(DisclosureParsingHelper).GetMethod(
            "IsTransactionTable",
            BindingFlags.NonPublic | BindingFlags.Static
        );
        var headers = new List<string> { "owner", "asset name", "amount" };

        var result = (bool)method!.Invoke(null, [headers]);

        result.Should().BeFalse();
    }
}
