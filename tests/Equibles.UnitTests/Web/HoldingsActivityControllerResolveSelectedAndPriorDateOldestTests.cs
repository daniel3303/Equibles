using System.Reflection;
using Equibles.Web.Controllers;

namespace Equibles.UnitTests.Web;

/// <summary>
/// Pins the boundary where the user requests the oldest available report date.
/// ResolveSelectedAndPriorDate must return previous = null — there is no older
/// quarter. A refactor that dropped the bounds check (selectedIndex &lt;
/// reportDates.Count - 1) would throw IndexOutOfRangeException, and one that
/// always returned a prior date would fabricate a stale comparison quarter.
/// </summary>
public class HoldingsActivityControllerResolveSelectedAndPriorDateOldestTests
{
    private static readonly MethodInfo ResolveMethod = typeof(HoldingsActivityController).GetMethod(
        "ResolveSelectedAndPriorDate",
        BindingFlags.NonPublic | BindingFlags.Static
    );

    [Fact]
    public void ResolveSelectedAndPriorDate_RequestedIsOldestDate_PreviousIsNull()
    {
        var reportDates = new List<DateOnly>
        {
            new(2024, 12, 31),
            new(2024, 9, 30),
            new(2024, 6, 30),
        };
        DateOnly? requested = new(2024, 6, 30);

        var result =
            (ValueTuple<DateOnly, DateOnly?>)ResolveMethod.Invoke(null, [requested, reportDates]);

        result.Item1.Should().Be(new DateOnly(2024, 6, 30), "selected must be the requested date");
        result.Item2.Should().BeNull("no quarter precedes the oldest report date");
    }
}
