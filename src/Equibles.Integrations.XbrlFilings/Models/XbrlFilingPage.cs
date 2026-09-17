namespace Equibles.Integrations.XbrlFilings.Models;

// One page of the index plus the total the host states for the whole filter, so a caller knows when to stop.
public sealed record XbrlFilingPage(IReadOnlyList<XbrlFiling> Filings, int TotalCount);
