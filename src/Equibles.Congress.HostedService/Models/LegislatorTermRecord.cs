namespace Equibles.Congress.HostedService.Models;

/// <summary>
/// One term as published: "rep" or "sen", the dates it covers, and the district (0 for an
/// at-large seat or a delegate; absent for a senator).
/// </summary>
public class LegislatorTermRecord
{
    public string Type { get; set; }
    public DateOnly? Start { get; set; }
    public DateOnly? End { get; set; }
    public string State { get; set; }
    public int? District { get; set; }
    public string Party { get; set; }
    public string Caucus { get; set; }
    public List<LegislatorPartyAffiliationRecord> PartyAffiliations { get; set; } = [];
}
