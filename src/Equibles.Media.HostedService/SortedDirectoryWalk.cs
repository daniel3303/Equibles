namespace Equibles.Media.HostedService;

/// <summary>
/// Walks a directory tree in a stable order, child folders sorted ordinally and each folder
/// visited after its children, and resumes after a folder an earlier walk finished: that folder,
/// its subtree and everything before it are skipped without being listed. Visiting children
/// first lets a visitor remove a folder once they are gone.
/// </summary>
internal static class SortedDirectoryWalk
{
    /// <param name="resumeAfter">The last folder an earlier walk finished, '/'-separated and relative to <paramref name="root"/>; null or empty walks everything.</param>
    /// <param name="include">Whether to enter a folder, given its relative path segments; the root is always entered.</param>
    /// <param name="visit">Receives each folder's full path and relative segments (none for the root) once its children are done.</param>
    public static Task Walk(
        string root,
        string resumeAfter,
        Func<IReadOnlyList<string>, bool> include,
        Func<string, IReadOnlyList<string>, Task> visit,
        CancellationToken cancellationToken
    )
    {
        var cursor = string.IsNullOrEmpty(resumeAfter) ? null : resumeAfter.Split('/');
        return WalkDirectory(root, [], cursor, include, visit, cancellationToken);
    }

    private static async Task WalkDirectory(
        string fullPath,
        IReadOnlyList<string> segments,
        string[] cursor,
        Func<IReadOnlyList<string>, bool> include,
        Func<string, IReadOnlyList<string>, Task> visit,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var children = Directory
            .EnumerateDirectories(fullPath)
            .Select(Path.GetFileName)
            .Order(StringComparer.Ordinal)
            .ToList();
        foreach (var child in children)
        {
            string[] childSegments = [.. segments, child];
            if (cursor != null && ComparePostOrder(childSegments, cursor) <= 0)
            {
                continue;
            }

            if (include(childSegments))
            {
                await WalkDirectory(
                    Path.Combine(fullPath, child),
                    childSegments,
                    cursor,
                    include,
                    visit,
                    cancellationToken
                );
            }
        }

        if (cursor == null || ComparePostOrder(segments, cursor) > 0)
        {
            await visit(fullPath, segments);
        }
    }

    /// <summary>
    /// Orders two folders by when this walk finishes them: a descendant finishes before its
    /// ancestor, otherwise the first differing name decides.
    /// </summary>
    internal static int ComparePostOrder(IReadOnlyList<string> a, IReadOnlyList<string> b)
    {
        var shared = Math.Min(a.Count, b.Count);
        for (var i = 0; i < shared; i++)
        {
            var order = string.CompareOrdinal(a[i], b[i]);
            if (order != 0)
            {
                return order;
            }
        }

        return b.Count.CompareTo(a.Count);
    }
}
