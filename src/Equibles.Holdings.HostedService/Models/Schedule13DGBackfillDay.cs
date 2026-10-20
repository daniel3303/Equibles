namespace Equibles.Holdings.HostedService.Models;

/// <summary>One daily index of the 13D/13G coverage walk; incomplete when EDGAR stopped answering.</summary>
public record Schedule13DGBackfillDay(bool Completed, int Documents, int Imported);
