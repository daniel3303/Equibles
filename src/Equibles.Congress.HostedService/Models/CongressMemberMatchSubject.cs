using Equibles.Congress.Data.Models;

namespace Equibles.Congress.HostedService.Models;

/// <summary>A filing member as the legislator matcher sees it: filed name, seat and filing dates.</summary>
public sealed record CongressMemberMatchSubject(
    string Name,
    CongressPosition Position,
    string StateDistrict,
    IReadOnlyCollection<DateOnly> FilingDates
);
