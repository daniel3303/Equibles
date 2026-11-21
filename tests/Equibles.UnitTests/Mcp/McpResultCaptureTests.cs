using System.Threading.Tasks;
using Equibles.Mcp;

namespace Equibles.UnitTests.Mcp;

// A host reads what a tool recorded only through the scope it opened before dispatching the call;
// a record made inside the awaited tool must still reach it, and no scope means nothing is kept.
public class McpResultCaptureTests
{
    private sealed record Shown(int Rows);

    [Fact]
    public async Task Record_InsideAnAwaitedTool_ReachesTheScopeTheHostOpened()
    {
        using var scope = McpResultCapture.Begin();

        await Task.Run(async () =>
        {
            await Task.Yield();
            McpResultCapture.Record(new Shown(3));
        });

        Assert.Equal(new Shown(3), scope.Get<Shown>());
    }

    [Fact]
    public void Record_WithNoScopeOpen_KeepsNothing()
    {
        McpResultCapture.Record(new Shown(1));

        using var scope = McpResultCapture.Begin();
        Assert.Empty(scope.Results);
    }

    [Fact]
    public void Dispose_RestoresTheOuterScope()
    {
        using var outer = McpResultCapture.Begin();
        using (McpResultCapture.Begin())
        {
            McpResultCapture.Record(new Shown(1));
        }

        McpResultCapture.Record(new Shown(2));

        Assert.Equal(new Shown(2), outer.Get<Shown>());
        Assert.Single(outer.Results);
    }
}
