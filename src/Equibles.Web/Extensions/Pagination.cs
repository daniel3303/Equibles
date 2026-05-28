namespace Equibles.Web.Extensions;

public static class Pagination
{
    // Ceiling division with a floor of 1 — pages never collapse to 0 even on empty datasets.
    public static int PageCount(int totalCount, int pageSize) =>
        Math.Max(1, (totalCount + pageSize - 1) / pageSize);
}
