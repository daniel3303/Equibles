using System.Reflection;
using Equibles.Congress.HostedService.Services;

namespace Equibles.UnitTests.Congress;

public class DisclosureParsingHelperIsTransactionTableMissingAssetTests
{
    // IsTransactionTable gates the entire ParseTransactionTable iterator —
    // when it returns false the parser yields nothing. The contract is that
    // a table qualifies ONLY when it has BOTH a date-like column AND an
    // asset/ticker/description column (the body literally returns
    // `hasDate && hasAsset`). Pin the AND-ing on a date-only header set: a
    // refactor that flipped `&&` to `||` (a single-character typo) would
    // silently accept malformed tables that contain only a date column,
    // routing the next step (ParseTransactionRow) into a column-index
    // lookup against absent columns — producing rows with empty tickers
    // that pollute the congressional-trade stream.
    [Fact]
    public void IsTransactionTable_DateColumnOnlyNoAsset_ReturnsFalse()
    {
        var method = typeof(DisclosureParsingHelper).GetMethod(
            "IsTransactionTable",
            BindingFlags.NonPublic | BindingFlags.Static
        );
        var headers = new List<string> { "transaction date", "owner", "amount" };

        var result = (bool)method.Invoke(null, [headers]);

        result.Should().BeFalse();
    }
}
