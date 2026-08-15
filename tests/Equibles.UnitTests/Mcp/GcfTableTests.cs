using System;
using System.Collections.Generic;
using BlackwellSystems.Gcf;
using Equibles.Mcp.Helpers;

namespace Equibles.UnitTests.Mcp;

// Shares the EQUIBLES_OUTPUT_FORMAT environment variable with MarkdownTableGcfTests;
// the collection keeps the two classes from mutating it in parallel.
[Collection("EquiblesOutputFormatEnv")]
public class GcfTableTests : IDisposable
{
    public GcfTableTests() => Environment.SetEnvironmentVariable("EQUIBLES_OUTPUT_FORMAT", null);

    public void Dispose() => Environment.SetEnvironmentVariable("EQUIBLES_OUTPUT_FORMAT", null);

    [Fact]
    public void Enabled_Reflects_Environment()
    {
        Assert.False(GcfTable.Enabled);

        foreach (var value in new[] { "gcf", "GCF", " gcf " })
        {
            Environment.SetEnvironmentVariable("EQUIBLES_OUTPUT_FORMAT", value);
            Assert.True(GcfTable.Enabled);
        }

        Environment.SetEnvironmentVariable("EQUIBLES_OUTPUT_FORMAT", "json");
        Assert.False(GcfTable.Enabled);
    }

    [Fact]
    public void TryEncode_Emits_Gcf_And_RoundTrips_Losslessly()
    {
        var header = "| Date | Short Volume | Total Volume | Short % |";
        var rows = new List<string>
        {
            "| 2026-08-15 | 12,345,678 | 45,678,901 | 42.5% |",
            "| 2026-08-16 | 9,000,000 | 24,000,000 | 37.5% |",
        };

        var wire = GcfTable.TryEncode(header, rows);

        Assert.NotNull(wire);
        Assert.StartsWith("GCF profile=generic", wire);
        // Header names are factored once; the pipe padding and separator row are gone.
        Assert.Contains("Short Volume", wire);
        Assert.DoesNotContain("| ---", wire);
        // Decoding and re-encoding reproduces the same wire (lossless round-trip).
        Assert.Equal(wire, Gcf.EncodeGeneric(Gcf.DecodeGeneric(wire)));
    }

    [Fact]
    public void TryEncode_Preserves_Exact_Cell_Formatting()
    {
        // The maintainer's compact-USD, comma grouping, em-dash and percent formatting
        // must survive verbatim: GCF changes the framing, not the values.
        var header = "| Institution | Shares | Value | % Out |";
        var rows = new List<string> { "| Vanguard | 123,456,789 | $1.23B | — |" };

        var wire = GcfTable.TryEncode(header, rows);

        Assert.NotNull(wire);
        Assert.Contains("123,456,789", wire);
        Assert.Contains("$1.23B", wire);
        Assert.Contains("—", wire);
    }

    [Fact]
    public void TryEncode_Returns_Null_On_Column_Count_Mismatch()
    {
        var header = "| A | B | C |";
        var rows = new List<string> { "| 1 | 2 |" }; // two cells, header has three

        Assert.Null(GcfTable.TryEncode(header, rows));
    }

    [Fact]
    public void TryEncode_Unescapes_Data_Pipes()
    {
        // MarkdownTable.EscapeCell writes a data pipe as "\|"; it must decode back to "|",
        // not split into an extra column.
        var header = "| Name | Note |";
        var rows = new List<string> { @"| Acme | a \| b |" };

        var wire = GcfTable.TryEncode(header, rows);

        Assert.NotNull(wire);
        var decoded = (System.Collections.IList)Gcf.DecodeGeneric(wire);
        var first = (OrderedMap)decoded[0];
        Assert.Equal("a | b", first["Note"]);
    }
}
