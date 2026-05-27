using System.Reflection;
using Equibles.Web.Controllers;
using Equibles.Web.ViewModels.Stocks;

namespace Equibles.UnitTests.Web;

public class StocksControllerParsePositionTypesMixedValidInvalidTests
{
    [Fact]
    public void ParsePositionTypes_MixedValidAndInvalid_KeepsOnlyDefinedMembersCaseInsensitive()
    {
        // Companion to StocksControllerParsePositionTypesUndefinedNumericTests, which
        // pins the all-invalid path (returns null). This pin covers the surviving-valid
        // path: when the comma-separated input mixes a defined name ("new", lower-case),
        // a numeric value with no matching member ("999"), an unrecognised name ("foo")
        // and another defined name ("Increased"), the parser must drop the two invalid
        // tokens and return a set containing exactly the two valid members. Anything
        // looser would leak (PositionChangeType)999 (or a parsed-as-name garbage value)
        // into the bucket-filter set the UI/CSV exports trust.
        var method = typeof(StocksController).GetMethod(
            "ParsePositionTypes",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var result = (HashSet<PositionChangeType>)method.Invoke(null, ["new,999,foo,Increased"]);

        result.Should().NotBeNull();
        result
            .Should()
            .BeEquivalentTo(new[] { PositionChangeType.New, PositionChangeType.Increased });
    }
}
