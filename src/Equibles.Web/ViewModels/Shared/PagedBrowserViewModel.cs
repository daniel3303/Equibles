using Equibles.Web.Extensions;

namespace Equibles.Web.ViewModels.Shared;

// Shared paging state for the search/browse list pages. TotalPages floors at 1 via
// Pagination.PageCount so an empty result set still renders a single (empty) page.
public abstract class PagedBrowserViewModel
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
    public int TotalCount { get; set; }
    public int TotalPages => Pagination.PageCount(TotalCount, PageSize);
}
