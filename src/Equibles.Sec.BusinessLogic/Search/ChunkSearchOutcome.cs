using Equibles.Sec.Data.Models.Chunks;

namespace Equibles.Sec.BusinessLogic.Search;

/// <summary>
/// A chunk search's answer plus whether a keyword pass ran out of budget before it finished.
/// Incomplete chunks are the best-ranked matches found so far, so more may exist and a shortfall
/// against the requested count does not mean nothing else matched.
/// </summary>
public sealed record ChunkSearchOutcome(List<Chunk> Chunks, bool Incomplete);
