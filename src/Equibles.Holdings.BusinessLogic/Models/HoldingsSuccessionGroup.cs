namespace Equibles.Holdings.BusinessLogic.Models;

// Filers whose 13F reports continue under other CIKs from ReportDate, keyed by holder id with
// their names. Comparable groups are compared as one holder across that quarter.
public sealed record HoldingsSuccessionGroup(
    DateOnly ReportDate,
    IReadOnlyDictionary<Guid, string> Predecessors,
    IReadOnlyDictionary<Guid, string> Successors,
    bool Comparable
)
{
    // One successor carries the whole group, so each holder's change is its successor's.
    public bool SingleSuccessor => Successors.Count == 1;

    public bool Contains(Guid holderId) =>
        Predecessors.ContainsKey(holderId) || Successors.ContainsKey(holderId);
}
