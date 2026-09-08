using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Equibles.Mcp.Helpers;

namespace Equibles.UnitTests.Mcp;

// The per-request output format. EQUIBLES_OUTPUT_FORMAT is process-wide, so on a server
// shared by many callers it can only be on for everyone or no one; the scope is what lets
// one client opt in. Shares that variable with the other GCF suites, hence the collection.
[Collection("EquiblesOutputFormatEnv")]
public class OutputFormatScopeTests : IDisposable
{
    public OutputFormatScopeTests() =>
        Environment.SetEnvironmentVariable("EQUIBLES_OUTPUT_FORMAT", null);

    public void Dispose() => Environment.SetEnvironmentVariable("EQUIBLES_OUTPUT_FORMAT", null);

    [Fact]
    public void No_Scope_And_No_Environment_Is_Markdown()
    {
        Assert.Null(OutputFormatScope.Current);
        Assert.False(GcfTable.Enabled);
    }

    [Fact]
    public void Scope_Enables_Gcf_Without_The_Environment_Variable()
    {
        using (OutputFormatScope.Enter(McpOutputFormat.Gcf))
        {
            Assert.True(GcfTable.Enabled);
        }

        Assert.False(GcfTable.Enabled);
    }

    // The other direction matters as much: a server that defaults to GCF must still let a
    // caller ask for the markdown its client can render.
    [Fact]
    public void Scope_Asking_For_Markdown_Overrides_A_Gcf_Environment_Default()
    {
        Environment.SetEnvironmentVariable("EQUIBLES_OUTPUT_FORMAT", "gcf");

        using (OutputFormatScope.Enter(McpOutputFormat.Markdown))
        {
            Assert.False(GcfTable.Enabled);
        }

        Assert.True(GcfTable.Enabled, "the process default is restored once the request ends");
    }

    [Fact]
    public void Nested_Scopes_Restore_The_Enclosing_Value()
    {
        using (OutputFormatScope.Enter(McpOutputFormat.Gcf))
        {
            using (OutputFormatScope.Enter(McpOutputFormat.Markdown))
            {
                Assert.Equal(McpOutputFormat.Markdown, OutputFormatScope.Current);
            }

            Assert.Equal(McpOutputFormat.Gcf, OutputFormatScope.Current);
        }

        Assert.Null(OutputFormatScope.Current);
    }

    // The property the whole design rests on: a host enters the scope, then awaits the
    // dispatch that eventually renders a table, possibly on another thread. Without this
    // flow the format would be read back as the process default.
    [Fact]
    public async Task Scope_Flows_Across_Awaits_And_Into_A_Started_Task()
    {
        using (OutputFormatScope.Enter(McpOutputFormat.Gcf))
        {
            await Task.Yield();
            Assert.True(GcfTable.Enabled, "the format must survive an await");

            var observed = await Task.Run(() => GcfTable.Enabled);
            Assert.True(observed, "and must reach work started inside the request");
        }
    }

    // A scope entered inside a task must not escape into the caller, or one request's
    // preference would bleed into the next one handled by that thread.
    [Fact]
    public async Task A_Scope_Entered_Inside_A_Task_Does_Not_Escape_To_The_Caller()
    {
        await Task.Run(() =>
        {
            OutputFormatScope.Enter(McpOutputFormat.Gcf);
            Assert.True(GcfTable.Enabled);
        });

        Assert.False(GcfTable.Enabled);
    }

    [Theory]
    [InlineData("gcf", McpOutputFormat.Gcf)]
    [InlineData("GCF", McpOutputFormat.Gcf)]
    [InlineData("  gcf  ", McpOutputFormat.Gcf)]
    [InlineData("markdown", McpOutputFormat.Markdown)]
    [InlineData("md", McpOutputFormat.Markdown)]
    public void TryParse_Reads_The_Known_Spellings(string value, McpOutputFormat expected)
    {
        Assert.True(OutputFormatScope.TryParse(value, out var format));
        Assert.Equal(expected, format);
    }

    // A typo must leave the server's own default alone rather than silently selecting
    // markdown, which on a GCF-default server would change every answer it returns.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("gcf2")]
    [InlineData("json")]
    public void TryParse_Refuses_An_Unrecognized_Value(string value)
    {
        Assert.False(OutputFormatScope.TryParse(value, out _));
    }

    [Fact]
    public void An_Unrecognized_Environment_Value_Leaves_Output_As_Markdown()
    {
        Environment.SetEnvironmentVariable("EQUIBLES_OUTPUT_FORMAT", "gcf2");

        Assert.False(GcfTable.Enabled);
    }

    // The scope has to reach the renderer, not just the flag.
    [Fact]
    public void A_Scoped_Request_Renders_A_Gcf_Wire()
    {
        IReadOnlyList<(string Date, string Vol)> rows = new[]
        {
            ("2026-08-15", "12,345,678"),
            ("2026-08-16", "9,000,000"),
        };

        string Render() =>
            MarkdownTable.Render(
                rows,
                "No data.",
                "Daily volume for AAPL:",
                "| Date | Volume |",
                "|------|--------|",
                r => $"| {r.Date} | {r.Vol} |"
            );

        Assert.DoesNotContain("GCF profile=generic", Render());

        using (OutputFormatScope.Enter(McpOutputFormat.Gcf))
        {
            var output = Render();

            Assert.Contains("GCF profile=generic", output);
            Assert.Contains("12,345,678", output);
        }
    }
}
