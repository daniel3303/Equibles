namespace Equibles.Holdings.HostedService.Models;

/// <summary>What happened to one parsed 13D/13G handed to the import pipeline.</summary>
public enum Schedule13DGImportOutcome
{
    /// <summary>The position was stored and the accession recorded as processed.</summary>
    Imported,

    /// <summary>A later stored filing reports the same stake; only the details were kept.</summary>
    Superseded,

    /// <summary>The import asked to be retried later, typically an untracked CUSIP.</summary>
    Incomplete,

    /// <summary>The import completed but stored no position; left unrecorded for a retry.</summary>
    NothingInserted,

    /// <summary>The import threw; left unrecorded for a retry.</summary>
    Failed,
}
