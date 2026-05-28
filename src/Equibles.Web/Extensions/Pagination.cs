namespace Equibles.Web.Extensions;

public static class Pagination
{
    // Ceiling division with a floor of 1 — pages never collapse to 0 even on empty datasets.
    public static int PageCount(int totalCount, int pageSize) =>
        Math.Max(1, (totalCount + pageSize - 1) / pageSize);

    // Client-supplied page param: a non-positive value yields a negative OFFSET in
    // Skip((page-1)*pageSize), which PostgreSQL rejects (22023) and surfaces as HTTP 500.
    public static int ClampPage(int page) => page < 1 ? 1 : page;

    public static IQueryable<T> Page<T>(this IQueryable<T> source, int page, int pageSize) =>
        source.Skip((page - 1) * pageSize).Take(pageSize);
}
