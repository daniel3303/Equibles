using Xunit;

namespace Equibles.UnitTests.Mcp;

// The README's tool count is the number this project gets compared on: every "best MCP server for
// stock data" roundup ranks entrants by it, and the AI assistants that answer that question read
// the README to do it. Written out as a literal it silently stops being true the day a tool lands.
//
// Only the SELF-HOSTED count is asserted, because it is the only one this repo can prove. The README
// used to also state the hosted server's total and the difference between the two; both drifted, so
// it now says Cloud "adds more on top" rather than quoting a number nothing here can keep honest.
public class PublishedToolCountTests
{
    [Fact]
    public void Readme_StatesTheNumberOfToolsThisBuildActuallyServes()
    {
        var served = McpToolAnnotationsTests.EnumerateTools().Count;
        var readme = File.ReadAllText(Path.Combine(RepositoryRoot(), "README.md"));

        // Both places the figure is published. Update them together with the tool that changed it.
        readme
            .Should()
            .Contain(
                $"**{served} MCP tools**",
                "the README badge line publishes this build's tool count"
            )
            .And.Contain(
                $"exposes {served} tools over MCP",
                "the Tools section publishes this build's tool count"
            );
    }

    private static string RepositoryRoot()
    {
        // Anchored on the solution file, not on README.md: tests/README.md sits between the test
        // binaries and the root, so a walk looking for any README stops one directory too early.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Equibles.sln")))
        {
            dir = dir.Parent;
        }

        dir.Should()
            .NotBeNull("the test must run from inside the repository so it can read the README");
        return dir!.FullName;
    }
}
