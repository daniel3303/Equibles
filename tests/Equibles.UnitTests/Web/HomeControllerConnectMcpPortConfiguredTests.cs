using Equibles.Web.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Equibles.UnitTests.Web;

public class HomeControllerConnectMcpPortConfiguredTests
{
    // Sibling to `Connect_McpPortNotConfigured_BuildsMcpUrlWithDefaultPort8081`.
    // That pin exercises ONLY the `?? "8081"` default-coalesce arm — i.e. the
    // production scenario where McpPort is NOT in IConfiguration. A regression
    // that hard-coded the port (`var mcpPort = "8081";`) or swapped the
    // configuration key (`_configuration["Mcp:Port"]` vs flat `McpPort`)
    // would compile cleanly, pass the existing default-arm sibling (which
    // supplies an empty configuration), and silently ignore every operator's
    // `MCP_PORT` env-var override. Production hosts that re-map the MCP port
    // (Docker port collision, reverse-proxy convention) would see Connect
    // hand users the wrong URL with no failure signal.
    //
    // Pin the configured-arm: feed an explicit non-default port via
    // IConfiguration, assert ViewData["McpUrl"] uses THAT value. The
    // assertion's port must be visibly distinct from the 8081 default to
    // distinguish "configured value flows through" from "default was
    // emitted regardless".
    [Fact]
    public void Connect_McpPortConfigured_BuildsMcpUrlWithConfiguredPortNotDefault()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Scheme = "http";
        httpContext.Request.Host = new HostString("localhost");

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string> { ["McpPort"] = "9090" })
            .Build();

        var controller = new HomeController(NullLogger<HomeController>.Instance, configuration)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext },
            TempData = Substitute.For<ITempDataDictionary>(),
        };

        var result = controller.Connect();

        result.Should().BeOfType<ViewResult>();
        controller.ViewData["McpUrl"].Should().Be("http://localhost:9090/mcp");
    }
}
