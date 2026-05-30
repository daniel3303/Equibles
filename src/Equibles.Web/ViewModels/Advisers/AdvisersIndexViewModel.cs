using Equibles.Sec.Data.Models;

namespace Equibles.Web.ViewModels.Advisers;

public class AdvisersIndexViewModel
{
    public string Query { get; set; }
    public List<FormAdvAdviser> Advisers { get; set; } = [];
    public int Page { get; set; }
    public int TotalPages { get; set; }
    public int TotalCount { get; set; }
}
