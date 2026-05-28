using Equibles.Holdings.Repositories.Models;
using Equibles.Web.Extensions;

namespace Equibles.Web.ViewModels.Holdings;

public class DoubleDownViewModel : QuarterlySelectionViewModel
{
    public const int PageSize = 100;
    public const double DefaultMinPct = 50.0;

    public double MinPctIncrease { get; set; } = DefaultMinPct;

    public List<DoubleDownPosition> Positions { get; set; } = [];
    public int Page { get; set; } = 1;
    public int TotalCount { get; set; }
    public int TotalPages => Pagination.PageCount(TotalCount, PageSize);
}
