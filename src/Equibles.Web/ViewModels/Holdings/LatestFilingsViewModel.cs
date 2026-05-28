using Equibles.Holdings.Repositories.Models;
using Equibles.Web.Extensions;

namespace Equibles.Web.ViewModels.Holdings;

public class LatestFilingsViewModel
{
    public const int PageSize = 50;

    public List<RecentFiling> Filings { get; set; } = [];
    public int Page { get; set; } = 1;
    public int TotalCount { get; set; }
    public int TotalPages => Pagination.PageCount(TotalCount, PageSize);
}
