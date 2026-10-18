namespace Equibles.Sec.BusinessLogic.Search;

/// <summary>
/// Optional deployment seam: supplies a short plain-language summary of what each stored
/// document says, so filing lists can show it beside the form and dates. The framework
/// registers no implementation; a deployment that summarizes documents registers its own,
/// and every list renders without summaries when none is registered (consumers take this
/// as an optional constructor dependency).
/// </summary>
public interface IDocumentSummaryReader
{
    /// <summary>
    /// The available summaries for <paramref name="documentIds"/>, keyed by document id.
    /// A document without a summary is absent from the result, never mapped to null.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, string>> Read(
        IReadOnlyCollection<Guid> documentIds,
        CancellationToken cancellationToken = default
    );
}
