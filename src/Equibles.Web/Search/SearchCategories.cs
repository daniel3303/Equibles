namespace Equibles.Web.Search;

/// <summary>
/// Canonical, display-ordered list of the categories the global search can return. These strings
/// must match each module's <c>ISearchProvider.Category</c> verbatim — the search view compares
/// them with <c>OrdinalIgnoreCase</c> to drive the scope selector and the post-results chips.
/// Kept in the Web layer (a presentation concern, like <see cref="Extensions.SearchCategoryRouteExtensions"/>)
/// so providers stay MVC-free; ordering mirrors each provider's <c>Order</c>.
/// </summary>
public static class SearchCategories
{
    public static readonly IReadOnlyList<string> All =
    [
        "Stocks",
        "SEC Filings",
        "Economic Indicators",
        "Institutions",
        "Insiders",
        "Congress",
        "Futures",
    ];
}
