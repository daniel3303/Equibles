using Equibles.Media.HostedService;

namespace Equibles.UnitTests.Media;

public class SortedDirectoryWalkTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "eq-walk-" + Guid.NewGuid().ToString("N")
    );

    public SortedDirectoryWalkTests()
    {
        foreach (var folder in new[] { "b/2", "b/1", "a/x", "c" })
        {
            Directory.CreateDirectory(Path.Combine(_root, folder));
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private async Task<List<string>> Visited(
        string resumeAfter,
        Func<IReadOnlyList<string>, bool> include = null
    )
    {
        var visited = new List<string>();
        await SortedDirectoryWalk.Walk(
            _root,
            resumeAfter,
            include ?? (_ => true),
            (_, folder) =>
            {
                visited.Add(string.Join('/', folder));
                return Task.CompletedTask;
            },
            CancellationToken.None
        );
        return visited;
    }

    [Fact]
    public async Task Walk_FromTheStart_VisitsSortedChildrenBeforeTheirParent()
    {
        (await Visited(null)).Should().Equal("a/x", "a", "b/1", "b/2", "b", "c", "");
    }

    [Fact]
    public async Task Walk_AfterAFinishedFolder_SkipsItAndEverythingBeforeIt()
    {
        (await Visited("b/1")).Should().Equal("b/2", "b", "c", "");
    }

    [Fact]
    public async Task Walk_AfterAFinishedParent_SkipsItsWholeSubtree()
    {
        (await Visited("a")).Should().Equal("b/1", "b/2", "b", "c", "");
    }

    // The purge removes emptied folders, so a saved position can name one that is gone.
    [Fact]
    public async Task Walk_AfterAFolderSinceRemoved_ResumesAtTheNextOne()
    {
        Directory.Delete(Path.Combine(_root, "b", "1"));

        (await Visited("b/1")).Should().Equal("b/2", "b", "c", "");
    }

    [Fact]
    public async Task Walk_ExcludedFolder_IsNeitherEnteredNorVisited()
    {
        (await Visited(null, folder => folder[0] != "b")).Should().Equal("a/x", "a", "c", "");
    }

    [Fact]
    public async Task Walk_LinkedFolder_IsNotEntered()
    {
        Directory.CreateSymbolicLink(Path.Combine(_root, "c", "loop"), _root);

        (await Visited(null)).Should().Equal("a/x", "a", "b/1", "b/2", "b", "c", "");
    }

    [Theory]
    [InlineData("b/1", "b", -1)]
    [InlineData("b", "b/1", 1)]
    [InlineData("a/x", "b", -1)]
    [InlineData("b/2", "b/1", 1)]
    [InlineData("b/1", "b/1", 0)]
    public void ComparePostOrder_OrdersFoldersByWhenTheWalkFinishesThem(
        string a,
        string b,
        int expectedSign
    )
    {
        Math.Sign(SortedDirectoryWalk.ComparePostOrder(a.Split('/'), b.Split('/')))
            .Should()
            .Be(expectedSign);
    }
}
