using System.Reflection;
using Equibles.Web.Controllers;

namespace Equibles.UnitTests.Web;

public class ProfilesControllerResolveSelectedDateFallbackTests
{
    // ResolveSelectedDate accepts a user-controlled `date=` query parameter and
    // maps it against the available report-date list before the caller issues
    // a holdings lookup. The contract its name promises is fallback semantics:
    // an unknown requested date must NOT pass through unchecked, because the
    // downstream WHERE ReportDate = @date silently returns zero rows on an
    // unrecognised input and the page would render as "no data" instead of
    // showing the latest snapshot. A refactor that drops the `.Contains(...)`
    // guard (e.g. `requested.HasValue ? requested.Value : available[0]`)
    // compiles and passes any test that only exercises clean inputs — pin the
    // fallback explicitly so the guard cannot regress.
    [Fact]
    public void ResolveSelectedDate_RequestedDateNotInAvailable_FallsBackToFirstAvailable()
    {
        var available = new List<DateOnly>
        {
            new(2024, 12, 31),
            new(2024, 9, 30),
            new(2024, 6, 30),
        };
        DateOnly? requested = new(1999, 1, 1);

        var method = typeof(ProfilesController).GetMethod(
            "ResolveSelectedDate",
            BindingFlags.NonPublic | BindingFlags.Static
        );
        var result = (DateOnly)method.Invoke(null, [requested, available]);

        result.Should().Be(new DateOnly(2024, 12, 31));
    }
}
