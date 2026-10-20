namespace Equibles.Holdings.HostedService.Models;

/// <summary>One company block of an EDGAR submission header: its CIK and conformed name.</summary>
public record SecSubmissionParty(string Cik, string Name);
