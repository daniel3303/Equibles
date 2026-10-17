using Equibles.Holdings.Data.Models;

namespace Equibles.Holdings.BusinessLogic;

/// <summary>
/// Groups <see cref="HolderSuccession"/> pairs into the filers that continue together across
/// one quarter.
/// </summary>
public static class HoldingsSuccessionGroups
{
    /// <summary>
    /// Splits pairs into groups that share a predecessor or a successor within one successor
    /// quarter. A holder can succeed one filer and later be succeeded itself, so quarters never join.
    /// </summary>
    public static List<List<T>> Connect<T>(
        IEnumerable<T> pairs,
        Func<T, DateOnly> successorQuarter,
        Func<T, Guid> predecessor,
        Func<T, Guid> successor
    )
    {
        var groups = new List<List<T>>();
        foreach (var quarter in pairs.GroupBy(successorQuarter))
        {
            var root = new Dictionary<Guid, Guid>();
            Guid Find(Guid id)
            {
                while (root.TryGetValue(id, out var parent) && parent != id)
                    id = parent;
                return id;
            }
            foreach (var pair in quarter)
            {
                var from = Find(predecessor(pair));
                var to = Find(successor(pair));
                if (from != to)
                    root[from] = to;
            }
            groups.AddRange(
                quarter.GroupBy(pair => Find(successor(pair))).Select(group => group.ToList())
            );
        }
        return groups;
    }
}
