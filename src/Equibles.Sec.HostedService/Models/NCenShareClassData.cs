namespace Equibles.Sec.HostedService.Models;

/// <summary>One SHARES_OUTSTANDING row of an N-CEN data set.</summary>
public class NCenShareClassData
{
    public string ClassId { get; set; }
    public string ClassName { get; set; }
    public string Ticker { get; set; }
}
