namespace Equibles.Holdings.BusinessLogic.Contracts;

/// <summary>
/// The strategy categories one filed document states, each with the verbatim quote that states it.
/// </summary>
/// <param name="SourceName">The document's name as readers know it, e.g. "Form ADV Part 2A".</param>
/// <param name="AsOf">The document's own date.</param>
/// <param name="SourceUrl">A link to the original document; null when none is public.</param>
/// <param name="Statements">The stated categories, in the order the source gives them.</param>
public sealed record HolderStatedStrategy(
    string SourceName,
    DateOnly AsOf,
    string SourceUrl,
    IReadOnlyList<HolderStrategyStatement> Statements
);
