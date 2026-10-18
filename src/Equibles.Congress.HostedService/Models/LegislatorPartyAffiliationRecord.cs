namespace Equibles.Congress.HostedService.Models;

/// <summary>One party a legislator held for part of a term that saw a party switch.</summary>
public class LegislatorPartyAffiliationRecord
{
    public DateOnly? Start { get; set; }
    public DateOnly? End { get; set; }
    public string Party { get; set; }
}
