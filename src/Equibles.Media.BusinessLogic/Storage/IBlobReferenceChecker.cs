namespace Equibles.Media.BusinessLogic.Storage;

/// <summary>
/// Answers whether any live File row still references a stored blob by its
/// algorithm-prefixed content hash. The deletion sweep resolves every registered
/// checker and only retires a blob when ALL of them report it unreferenced — a
/// deployment that binds the Media module into more than one database registers one
/// checker per context so the sweep stays conservative.
/// </summary>
public interface IBlobReferenceChecker
{
    Task<bool> IsReferenced(string contentHash, CancellationToken cancellationToken);

    /// <summary>Returns the subset of <paramref name="contentHashes"/> that ARE referenced.</summary>
    Task<IReadOnlySet<string>> GetReferenced(
        IReadOnlyCollection<string> contentHashes,
        CancellationToken cancellationToken
    );
}
