using Equibles.Sec.Data.Models.Chunks;

namespace Equibles.Sec.Repositories;

/// <summary>
/// The BM25 chunk search hit its per-statement command timeout (Postgres cancelled the
/// statement). Distinct from an unknown fault so callers can degrade — run another pass, let
/// the vector arm answer — and distinct from an empty result, which would read as "the
/// filings say nothing about this" when the search in fact never finished.
/// </summary>
public class ChunkSearchTimeoutException : Exception
{
    public ChunkSearchTimeoutException(string message, Exception innerException)
        : base(message, innerException) { }

    public ChunkSearchTimeoutException(
        string message,
        Exception innerException,
        IReadOnlyList<Chunk> partialResults
    )
        : base(message, innerException)
    {
        PartialResults = partialResults ?? [];
    }

    /// <summary>
    /// The best-ranked in-scope matches a completed earlier round validated before the budget ran
    /// out, in rank order; possibly fewer than requested, and empty when no round completed.
    /// </summary>
    public IReadOnlyList<Chunk> PartialResults { get; } = [];
}
