namespace Equibles.Holdings.HostedService.Models;

/// <summary>The filed identifiers a holder's classification evidence is matched on.</summary>
public record HolderIdentityRow(Guid Id, string Cik, string CrdNumber, string SecFileNumber);
