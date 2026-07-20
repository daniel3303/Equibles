using System.Reflection;
using Equibles.Cboe.Mcp.Tools;
using Equibles.Cftc.Mcp.Tools;
using Equibles.Congress.Mcp.Tools;
using Equibles.FdaCatalysts.Mcp.Tools;
using Equibles.Finra.Mcp.Tools;
using Equibles.Fred.Mcp.Tools;
using Equibles.GovernmentContracts.Mcp.Tools;
using Equibles.Holdings.Mcp.Tools;
using Equibles.InsiderTrading.Mcp.Tools;
using Equibles.Sec.FinancialFacts.Mcp.Tools;
using Equibles.Sec.Mcp.Tools;
using Equibles.Yahoo.Mcp.Tools;
using ModelContextProtocol.Server;

namespace Equibles.UnitTests.Mcp;

// Every tool the server exposes must carry a display title and a behavioural hint.
// Clients surface the title in the connector consent screen — without one they fall
// back to the raw method name — and without ReadOnly they must assume a tool may
// mutate state, which turns a research-only server into a scary permission prompt.
// Connector directories reject servers whose tools omit either.
public class McpToolAnnotationsTests
{
    // One marker type per assembly that ships MCP tools.
    private static readonly Type[] ToolAssemblyMarkers =
    [
        typeof(CboeTools),
        typeof(CftcTools),
        typeof(CongressTools),
        typeof(FdaCatalystTools),
        typeof(ShortDataTools),
        typeof(FredTools),
        typeof(GovernmentContractsTools),
        typeof(InstitutionalHoldingsTools),
        typeof(InsiderTradingTools),
        typeof(FinancialFactsTools),
        typeof(RagSearchTools),
        typeof(StockPriceTools),
    ];

    [Fact]
    public void EveryTool_DeclaresATitle()
    {
        var untitled = EnumerateTools()
            .Where(t => string.IsNullOrWhiteSpace(t.Attribute.Title))
            .Select(t => $"{t.Method.DeclaringType!.Name}.{t.Method.Name}")
            .ToList();

        untitled
            .Should()
            .BeEmpty("every MCP tool needs a human-readable Title for the consent screen");
    }

    [Fact]
    public void EveryTool_DeclaresAReadOnlyHint()
    {
        // The server is research-only: it reads filings and market data and never writes.
        // A tool that genuinely mutates state should set Destructive instead, and this
        // assertion should then be narrowed rather than deleted.
        var unhinted = EnumerateTools()
            .Where(t => t.Attribute.ReadOnly != true)
            .Select(t => $"{t.Method.DeclaringType!.Name}.{t.Method.Name}")
            .ToList();

        unhinted.Should().BeEmpty("every tool on this server is read-only and must say so");
    }

    [Fact]
    public void ToolNames_StayWithinTheProtocolLengthLimit()
    {
        var overlong = EnumerateTools()
            .Select(t => t.Attribute.Name ?? t.Method.Name)
            .Where(name => name.Length > 64)
            .ToList();

        overlong.Should().BeEmpty("MCP tool names are capped at 64 characters");
    }

    [Fact]
    public void ToolTitles_AreUniqueAcrossTheServer()
    {
        // Two tools sharing a title are indistinguishable in the consent screen.
        var duplicates = EnumerateTools()
            .GroupBy(t => t.Attribute.Title)
            .Where(g => !string.IsNullOrWhiteSpace(g.Key) && g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        duplicates.Should().BeEmpty("each tool needs a distinguishable title");
    }

    [Fact]
    public void EnumerateTools_FindsTheWholeToolSurface()
    {
        // Guards the reflection itself: if a marker type is dropped or an assembly stops
        // being referenced, the assertions above would silently pass over nothing.
        EnumerateTools().Should().HaveCountGreaterThan(50);
    }

    private static List<(McpServerToolAttribute Attribute, MethodInfo Method)> EnumerateTools()
    {
        return ToolAssemblyMarkers
            .Select(marker => marker.Assembly)
            .Distinct()
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type.GetCustomAttribute<McpServerToolTypeAttribute>() != null)
            .SelectMany(type =>
                type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
            )
            .Select(method =>
                (Attribute: method.GetCustomAttribute<McpServerToolAttribute>(), Method: method)
            )
            .Where(t => t.Attribute != null)
            .Select(t => (t.Attribute!, t.Method))
            .ToList();
    }
}
