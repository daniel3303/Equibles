using Equibles.Holdings.Repositories.Models;

namespace Equibles.Web.ViewModels.Holdings;

public class LatestFilingsViewModel
{
    public const int PageSize = 50;

    public List<RecentFiling> Filings { get; set; } = [];
    public int Page { get; set; } = 1;
    public int TotalCount { get; set; }
    public int TotalPages => Math.Max(1, (TotalCount + PageSize - 1) / PageSize);
}
