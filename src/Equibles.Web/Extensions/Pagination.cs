namespace Equibles.Web.Extensions;

public static class Pagination
{
    // Ceiling division with a floor of 1 — pages never collapse to 0 even on empty datasets.
    public static int PageCount(int totalCount, int pageSize) =>
        Math.Max(1, (totalCount + pageSize - 1) / pageSize);

    // Client-supplied page param: a non-positive value yields a negative OFFSET in
    // Skip((page-1)*pageSize), which PostgreSQL rejects (22023) and surfaces as HTTP 500.
    public static int ClampPage(int page) => page < 1 ? 1 : page;

    public static IQueryable<T> Page<T>(this IQueryable<T> source, int page, int pageSize)
    {
        // Compute the offset in long so a maximal client page can't overflow Int32 into a
        // negative SQL OFFSET (PostgreSQL 22023 → HTTP 500); clamp to [0, int.MaxValue] so an
        // absurd page simply falls past the end and renders an empty page.
        var skip = (int)Math.Clamp((long)(page - 1) * pageSize, 0L, int.MaxValue);
        return source.Skip(skip).Take(pageSize);
    }
}
