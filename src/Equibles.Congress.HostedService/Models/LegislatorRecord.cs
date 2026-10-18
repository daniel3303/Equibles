namespace Equibles.Congress.HostedService.Models;

/// <summary>One legislator as published by the congress-legislators roster.</summary>
public class LegislatorRecord
{
    public LegislatorIdRecord Id { get; set; }
    public LegislatorNameRecord Name { get; set; }
    public List<LegislatorTermRecord> Terms { get; set; } = [];
}
