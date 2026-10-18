namespace Equibles.Congress.HostedService.Models;

/// <summary>The outcome of one seat-and-surname identity pass.</summary>
public class LegislatorIdentityReconciliation
{
    public int Assigned { get; set; }
    public List<LegislatorIdentityConflict> Conflicts { get; set; } = [];
}
