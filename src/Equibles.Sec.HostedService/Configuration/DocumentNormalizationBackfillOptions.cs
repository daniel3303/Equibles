namespace Equibles.Sec.HostedService.Configuration;

/// <summary>
/// Controls the self-draining sweep that re-derives stored filing text after a normalization
/// pipeline version bump.
/// </summary>
public class DocumentNormalizationBackfillOptions
{
    public bool Enabled { get; set; }

    public int BatchSize { get; set; } = 16;

    public int DrainIntervalSeconds { get; set; } = 60;

    /// <summary>
    /// The staged default handles periodic reports first because their fiscal tables feed RAG.
    /// Enable this only after the 10-K/10-Q stage drains.
    /// </summary>
    public bool IncludeAllDocumentTypes { get; set; }

    /// <summary>
    /// Accessions that must run before the staged queue, used to verify a known filing safely.
    /// </summary>
    public List<string> PriorityAccessions { get; set; } = [];
}
