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
}
