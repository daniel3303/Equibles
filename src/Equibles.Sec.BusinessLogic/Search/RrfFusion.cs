namespace Equibles.Sec.BusinessLogic.Search;

/// <summary>
/// Reciprocal Rank Fusion — the combiner ParadeDB documents for hybrid search. Each input list
/// is already in rank order; an item's fused score is the sum of 1/(k + rank) across the lists it
/// appears in. Postgres has no built-in RRF function, so this is the canonical hand-rolled
/// implementation. Used to merge the BM25 ranking with the semantic (vector) ranking.
/// </summary>
public static class RrfFusion
{
    public const int DefaultK = 60;

    public static List<Guid> Fuse(IEnumerable<IReadOnlyList<Guid>> rankedLists, int k = DefaultK)
    {
        var scores = new Dictionary<Guid, double>();
        foreach (var list in rankedLists)
        {
            for (var rank = 0; rank < list.Count; rank++)
            {
                var id = list[rank];
                scores.TryGetValue(id, out var current);
                scores[id] = current + 1.0 / (k + rank + 1);
            }
        }

        // Tie-break by chunk id so equal-score ties (common with short lists) resolve the same
        // way every run — dictionary enumeration order is otherwise unspecified.
        return scores
            .OrderByDescending(entry => entry.Value)
            .ThenBy(entry => entry.Key)
            .Select(entry => entry.Key)
            .ToList();
    }
}
