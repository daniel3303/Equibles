namespace Equibles.Congress.HostedService.Models;

/// <summary>A roster legislator left out of a sync, and why.</summary>
public sealed record LegislatorRejection(string BioguideId, string Reason);
