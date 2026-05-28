namespace Equibles.Web.Extensions;

public static class DateOnlyListExtensions
{
    // Returns the requested date when it appears in the list, otherwise the first
    // entry. Callers pass an already-ordered list (typically latest first) so the
    // fallback lands on the most recent available date. Throws if the list is
    // empty — guard against that at the call site.
    public static DateOnly ResolveSelectedDateOrFirst(
        this IList<DateOnly> available,
        DateOnly? requested
    ) => requested.HasValue && available.Contains(requested.Value) ? requested.Value : available[0];
}
