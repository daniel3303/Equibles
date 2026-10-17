using Equibles.Sec.Data.Models;

namespace Equibles.Sec.HostedService.Models;

/// <summary>One ADVISER row of an N-CEN data set.</summary>
public class NCenAdviserData
{
    public NCenAdviserRole Role { get; set; }
    public string Name { get; set; }
    public string FileNumber { get; set; }
    public int? Crd { get; set; }
    public string Lei { get; set; }
    public string State { get; set; }
    public string Country { get; set; }
    public bool? IsAffiliated { get; set; }
    public bool? IsHiredDuringPeriod { get; set; }
    public DateOnly? StartDate { get; set; }
    public DateOnly? TerminationDate { get; set; }
}
