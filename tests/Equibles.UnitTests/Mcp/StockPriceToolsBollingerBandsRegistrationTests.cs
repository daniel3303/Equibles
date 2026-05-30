using System.Reflection;
using Equibles.Yahoo.Mcp.Tools;
using FluentAssertions;

namespace Equibles.UnitTests.Mcp;

// The GetBollingerBands tool is a thin DB-backed wrapper over the (separately unit-tested)
// ComputeBollingerBands math, so there is no host-free seam to exercise its body. What we
// can pin without standing up the MCP host is its registration contract: the exposed tool
// name and the default parameters clients depend on. A rename or a changed default would
// silently break every caller, so this guards the boundary the same way the repo's other
// StockPriceTools test reflection-pins the shared projection helper.
public class StockPriceToolsBollingerBandsRegistrationTests
{
    private static readonly MethodInfo Method = typeof(StockPriceTools).GetMethod(
        "GetBollingerBands"
    );

    [Fact]
    public void GetBollingerBands_IsExposedAsAToolReturningAString()
    {
        Method.Should().NotBeNull();
        Method.ReturnType.Should().Be(typeof(Task<string>));

        // The MCP attribute is matched by type name to avoid a hard package reference here,
        // mirroring how the host discovers it.
        var toolAttribute = Method
            .GetCustomAttributes()
            .SingleOrDefault(a => a.GetType().Name == "McpServerToolAttribute");
        toolAttribute.Should().NotBeNull();

        var name = (string)toolAttribute.GetType().GetProperty("Name").GetValue(toolAttribute);
        name.Should().Be("GetBollingerBands");
    }

    [Fact]
    public void GetBollingerBands_DefaultsToTheConventionalPeriodAndDeviation()
    {
        var parameters = Method.GetParameters().ToDictionary(p => p.Name);

        parameters["period"].DefaultValue.Should().Be(20);
        parameters["stdDev"].DefaultValue.Should().Be(2m);
        parameters["maxResults"].DefaultValue.Should().Be(60);
    }
}
