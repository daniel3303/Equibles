using System.Reflection;
using Equibles.Web.Controllers;
using Equibles.Web.ViewModels.Stocks;

namespace Equibles.UnitTests.Web;

public class StocksControllerParsePositionTypesUndefinedNumericTests
{
    [Fact]
    public void ParsePositionTypes_NumericValueWithNoMatchingEnumMember_RejectsToPreventPollutedFilter()
    {
        // ParsePositionTypes binds the ?types=… query string for /stocks/{ticker}/holdings.
        // The defined PositionChangeType members are New=1, Increased=2, Reduced=3,
        // Unchanged=4, SoldOut=5. The contract a caller relies on is "the returned set
        // contains only defined enum members" — the result is rendered as filter chips,
        // round-tripped into toggle URLs (string.Join(",", next)) and used to gate
        // bucket counts, so a garbage value pollutes every downstream use of the set.
        //
        // The risk this catches: Enum.TryParse accepts numeric forms by default, so
        // ?types=999 succeeds and produces (PositionChangeType)999 with no matching
        // name. An unrecognised name like ?types=foo correctly yields null (no filter
        // applied) — the numeric form should follow the same rejection path. The fix
        // is a single Enum.IsDefined gate on the parsed value.
        var method = typeof(StocksController).GetMethod(
            "ParsePositionTypes",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var result = (HashSet<PositionChangeType>)method.Invoke(null, ["999"]);

        result
            .Should()
            .BeNull(
                "a numeric value with no matching enum member should be rejected the same as an unrecognised name"
            );
    }
}
