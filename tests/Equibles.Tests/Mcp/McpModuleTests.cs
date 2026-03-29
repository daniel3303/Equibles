using System.Reflection;
using Equibles.Fred.Mcp.Tools;
using Equibles.Holdings.Mcp.Tools;
using Equibles.InsiderTrading.Mcp.Tools;
using Equibles.Mcp;
using Equibles.Sec.Mcp.Tools;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;
using NSubstitute;

namespace Equibles.Tests.Mcp;

public class McpToolContextTests {
    [Fact]
    public void ToolName_CanBeSetAndRetrieved() {
        var context = new McpToolContext { ToolName = "TestTool" };

        context.ToolName.Should().Be("TestTool");
    }

    [Fact]
    public void Arguments_DefaultsToEmptyDictionary() {
        var context = new McpToolContext();

        context.Arguments.Should().NotBeNull();
        context.Arguments.Should().BeEmpty();
    }

    [Fact]
    public void Arguments_CanBeSetAndRetrieved() {
        var args = new Dictionary<string, object> {
            ["ticker"] = "AAPL",
            ["maxResults"] = 10
        };

        var context = new McpToolContext { Arguments = args };

        context.Arguments.Should().BeSameAs(args);
        context.Arguments.Should().HaveCount(2);
        context.Arguments["ticker"].Should().Be("AAPL");
        context.Arguments["maxResults"].Should().Be(10);
    }

    [Fact]
    public void ServiceProvider_CanBeSetAndRetrieved() {
        var serviceProvider = Substitute.For<IServiceProvider>();

        var context = new McpToolContext { ServiceProvider = serviceProvider };

        context.ServiceProvider.Should().BeSameAs(serviceProvider);
    }

    [Fact]
    public void ServiceProvider_DefaultsToNull() {
        var context = new McpToolContext();

        context.ServiceProvider.Should().BeNull();
    }

    [Fact]
    public void AllProperties_CanBeSetTogether() {
        var serviceProvider = Substitute.For<IServiceProvider>();
        var args = new Dictionary<string, object> { ["key"] = "value" };

        var context = new McpToolContext {
            ToolName = "MyTool",
            Arguments = args,
            ServiceProvider = serviceProvider
        };

        context.ToolName.Should().Be("MyTool");
        context.Arguments.Should().BeSameAs(args);
        context.ServiceProvider.Should().BeSameAs(serviceProvider);
    }
}

public class McpModuleInterfaceTests {
    [Theory]
    [MemberData(nameof(AllModuleTypes))]
    public void Module_ImplementsIEquiblesMcpModule(Type moduleType) {
        moduleType.Should().Implement<IEquiblesMcpModule>();
    }

    [Theory]
    [MemberData(nameof(AllModuleTypes))]
    public void Module_HasParameterlessConstructor(Type moduleType) {
        var constructor = moduleType.GetConstructor(Type.EmptyTypes);

        constructor.Should().NotBeNull(
            $"{moduleType.Name} must have a parameterless constructor for AddModule<T>()");
    }

    [Theory]
    [MemberData(nameof(AllModuleTypes))]
    public void Module_CanBeInstantiated(Type moduleType) {
        var module = Activator.CreateInstance(moduleType);

        module.Should().NotBeNull();
        module.Should().BeAssignableTo<IEquiblesMcpModule>();
    }

    [Theory]
    [MemberData(nameof(AllModuleTypes))]
    public void RegisterTools_DoesNotThrow(Type moduleType) {
        var module = (IEquiblesMcpModule)Activator.CreateInstance(moduleType)!;
        var mcpServerBuilder = Substitute.For<IMcpServerBuilder>();
        var services = new ServiceCollection();

        var act = () => module.RegisterTools(mcpServerBuilder, services);

        act.Should().NotThrow();
    }

    public static TheoryData<Type> AllModuleTypes =>
        new() {
            typeof(AssemblyMcpModule<FredTools>),
            typeof(AssemblyMcpModule<InstitutionalHoldingsTools>),
            typeof(AssemblyMcpModule<InsiderTradingTools>),
            typeof(AssemblyMcpModule<RagSearchTools>)
        };
}

public class McpModuleToolDiscoveryTests {
    [Theory]
    [MemberData(nameof(ToolAssembliesWithExpectedTools))]
    public void Module_ExposesExpectedTools(Type markerType, string[] expectedToolNames) {
        var toolNames = GetToolNamesFromAssembly(markerType.Assembly);

        toolNames.Should().Contain(expectedToolNames,
            $"{markerType.Name}'s assembly should expose all expected tools");
    }

    [Theory]
    [MemberData(nameof(ToolAssembliesWithExpectedTools))]
    public void Module_ToolCount_MatchesExpected(Type markerType, string[] expectedToolNames) {
        var toolNames = GetToolNamesFromAssembly(markerType.Assembly);

        toolNames.Should().HaveCount(expectedToolNames.Length,
            $"{markerType.Name}'s assembly should expose exactly {expectedToolNames.Length} tools");
    }

    [Theory]
    [MemberData(nameof(AllToolMarkerTypes))]
    public void Module_AllToolNames_AreNonEmpty(Type markerType) {
        var toolNames = GetToolNamesFromAssembly(markerType.Assembly);

        toolNames.Should().NotBeEmpty(
            $"{markerType.Name}'s assembly should expose at least one tool");

        foreach (var name in toolNames) {
            name.Should().NotBeNullOrWhiteSpace(
                $"all tool names in {markerType.Name}'s assembly should be non-empty");
        }
    }

    [Theory]
    [MemberData(nameof(AllToolMarkerTypes))]
    public void Module_AllToolClasses_HaveMcpServerToolTypeAttribute(Type markerType) {
        var toolTypes = markerType.Assembly.GetTypes()
            .Where(t => t.GetCustomAttribute<McpServerToolTypeAttribute>() != null);

        toolTypes.Should().NotBeEmpty(
            $"{markerType.Name}'s assembly should contain at least one [McpServerToolType] class");
    }

    [Theory]
    [MemberData(nameof(AllToolMarkerTypes))]
    public void Module_AllToolMethods_HaveDescriptionAttribute(Type markerType) {
        var toolMethods = markerType.Assembly.GetTypes()
            .Where(t => t.GetCustomAttribute<McpServerToolTypeAttribute>() != null)
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance))
            .Where(m => m.GetCustomAttribute<McpServerToolAttribute>() != null);

        foreach (var method in toolMethods) {
            var description = method.GetCustomAttributes()
                .FirstOrDefault(a => a.GetType().Name == "DescriptionAttribute");

            description.Should().NotBeNull(
                $"tool method {method.DeclaringType!.Name}.{method.Name} should have a [Description] attribute");
        }
    }

    [Theory]
    [MemberData(nameof(AllToolMarkerTypes))]
    public void Module_ToolNames_AreUnique(Type markerType) {
        var toolNames = GetToolNamesFromAssembly(markerType.Assembly);

        toolNames.Should().OnlyHaveUniqueItems(
            $"tool names in {markerType.Name}'s assembly should be unique");
    }

    // ── Fred module specific ────────────────────────────────────────────

    [Fact]
    public void FredModule_ExposesEconomicIndicatorTools() {
        var toolNames = GetToolNamesFromAssembly(typeof(FredTools).Assembly);

        toolNames.Should().Contain("GetEconomicIndicator");
        toolNames.Should().Contain("GetLatestEconomicData");
        toolNames.Should().Contain("SearchEconomicIndicators");
    }

    // ── Holdings module specific ────────────────────────────────────────

    [Fact]
    public void HoldingsModule_ExposesInstitutionalHoldingsTools() {
        var toolNames = GetToolNamesFromAssembly(typeof(InstitutionalHoldingsTools).Assembly);

        toolNames.Should().Contain("GetTopHolders");
        toolNames.Should().Contain("GetOwnershipHistory");
        toolNames.Should().Contain("GetInstitutionPortfolio");
        toolNames.Should().Contain("SearchInstitutions");
    }

    // ── InsiderTrading module specific ──────────────────────────────────

    [Fact]
    public void InsiderTradingModule_ExposesInsiderTradingTools() {
        var toolNames = GetToolNamesFromAssembly(typeof(InsiderTradingTools).Assembly);

        toolNames.Should().Contain("GetInsiderTransactions");
        toolNames.Should().Contain("GetInsiderOwnership");
        toolNames.Should().Contain("SearchInsiders");
    }

    // ── SEC module specific ─────────────────────────────────────────────

    [Fact]
    public void SecModule_ExposesSearchAndDocumentTools() {
        var toolNames = GetToolNamesFromAssembly(typeof(RagSearchTools).Assembly);

        toolNames.Should().Contain("SearchDocuments");
        toolNames.Should().Contain("SearchCompanyDocuments");
        toolNames.Should().Contain("SearchDocument");
        toolNames.Should().Contain("ListCompanyDocuments");
        toolNames.Should().Contain("SearchDocumentKeyword");
        toolNames.Should().Contain("ReadDocumentLines");
    }

    // ── Cross-module uniqueness ─────────────────────────────────────────

    [Fact]
    public void AllModules_ToolNames_AreGloballyUnique() {
        var allToolNames = new[] {
                typeof(FredTools),
                typeof(InstitutionalHoldingsTools),
                typeof(InsiderTradingTools),
                typeof(RagSearchTools)
            }
            .SelectMany(t => GetToolNamesFromAssembly(t.Assembly))
            .ToList();

        allToolNames.Should().OnlyHaveUniqueItems(
            "tool names across all MCP modules should be globally unique");
    }

    // ── Helpers ─────────────────────────────────────────────────────────

    private static List<string> GetToolNamesFromAssembly(Assembly assembly) {
        return assembly.GetTypes()
            .Where(t => t.GetCustomAttribute<McpServerToolTypeAttribute>() != null)
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance))
            .Select(m => m.GetCustomAttribute<McpServerToolAttribute>())
            .Where(attr => attr != null)
            .Select(attr => attr!.Name)
            .ToList();
    }

    public static TheoryData<Type, string[]> ToolAssembliesWithExpectedTools =>
        new() {
            {
                typeof(FredTools),
                new[] { "GetEconomicIndicator", "GetLatestEconomicData", "SearchEconomicIndicators" }
            },
            {
                typeof(InstitutionalHoldingsTools),
                new[] { "GetTopHolders", "GetOwnershipHistory", "GetInstitutionPortfolio", "SearchInstitutions" }
            },
            {
                typeof(InsiderTradingTools),
                new[] { "GetInsiderTransactions", "GetInsiderOwnership", "SearchInsiders" }
            },
            {
                typeof(RagSearchTools),
                new[] {
                    "SearchDocuments", "SearchCompanyDocuments", "SearchDocument",
                    "ListCompanyDocuments", "SearchDocumentKeyword", "ReadDocumentLines"
                }
            }
        };

    public static TheoryData<Type> AllToolMarkerTypes =>
        new() {
            typeof(FredTools),
            typeof(InstitutionalHoldingsTools),
            typeof(InsiderTradingTools),
            typeof(RagSearchTools)
        };
}
