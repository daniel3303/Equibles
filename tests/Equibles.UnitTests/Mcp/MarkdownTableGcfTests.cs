using System;
using System.Collections.Generic;
using Equibles.Mcp.Helpers;

namespace Equibles.UnitTests.Mcp;

[Collection("EquiblesOutputFormatEnv")]
public class MarkdownTableGcfTests : IDisposable
{
    private static readonly IReadOnlyList<(string Date, string Vol)> Rows = new[]
    {
        ("2026-08-15", "12,345,678"),
        ("2026-08-16", "9,000,000"),
    };

    private const string Header = "| Date | Volume |";
    private const string Separator = "|------|--------|";

    public MarkdownTableGcfTests() =>
        Environment.SetEnvironmentVariable("EQUIBLES_OUTPUT_FORMAT", null);

    public void Dispose() => Environment.SetEnvironmentVariable("EQUIBLES_OUTPUT_FORMAT", null);

    private static string Render() =>
        MarkdownTable.Render(
            Rows,
            "No data.",
            "Daily volume for AAPL:",
            Header,
            Separator,
            r => $"| {r.Date} | {r.Vol} |"
        );

    [Fact]
    public void Default_Output_Is_Unchanged_Markdown()
    {
        var output = Render();

        Assert.Contains("Daily volume for AAPL:", output);
        Assert.Contains(Header, output);
        Assert.Contains(Separator, output);
        Assert.Contains("| 2026-08-15 | 12,345,678 |", output);
        Assert.DoesNotContain("GCF profile=generic", output);
    }

    [Fact]
    public void Gcf_Enabled_Emits_Gcf_And_Keeps_Title()
    {
        Environment.SetEnvironmentVariable("EQUIBLES_OUTPUT_FORMAT", "gcf");

        var output = Render();

        Assert.StartsWith("Daily volume for AAPL:", output);
        Assert.Contains("GCF profile=generic", output);
        Assert.DoesNotContain(Separator, output); // markdown table framing is gone
        Assert.Contains("12,345,678", output); // exact cell formatting preserved
    }

    [Fact]
    public void Gcf_Enabled_Preserves_Title_And_Subtitle_Framing()
    {
        Environment.SetEnvironmentVariable("EQUIBLES_OUTPUT_FORMAT", "gcf");

        var output = MarkdownTable.Render(
            Rows,
            "No data.",
            "Daily volume for AAPL:",
            "Showing 2 of 2 rows.",
            Header,
            Separator,
            r => $"| {r.Date} | {r.Vol} |"
        );

        Assert.StartsWith("Daily volume for AAPL:\nShowing 2 of 2 rows.\n\n", output);
        Assert.Contains("GCF profile=generic", output);
        Assert.DoesNotContain(Separator, output);
    }

    [Fact]
    public void Gcf_Not_Used_When_It_Would_Grow_A_Tiny_Table()
    {
        Environment.SetEnvironmentVariable("EQUIBLES_OUTPUT_FORMAT", "gcf");

        // One short column, one row: GCF's `GCF profile=generic` + section-header overhead
        // exceeds the tiny markdown table, so the never-grow guard keeps the markdown.
        var output = MarkdownTable.Render(
            new[] { "x" },
            "No data.",
            "T:",
            "| C |",
            "|---|",
            r => $"| {r} |"
        );

        Assert.DoesNotContain("GCF profile=generic", output);
        Assert.Contains("| C |", output);
    }

    [Fact]
    public void Empty_Rows_Return_Empty_Message_Regardless_Of_Format()
    {
        Environment.SetEnvironmentVariable("EQUIBLES_OUTPUT_FORMAT", "gcf");

        var output = MarkdownTable.Render(
            Array.Empty<(string, string)>(),
            "No data.",
            "Daily volume for AAPL:",
            Header,
            Separator,
            r => "| x | y |"
        );

        Assert.Equal("No data.", output);
    }
}
