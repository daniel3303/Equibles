using Equibles.Mcp.Server;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol.Protocol;

namespace Equibles.UnitTests.Mcp;

public class McpServerTransportTests
{
    [Fact]
    public void ConfigureServices_Stdio_RegistersTheSingleSessionStdioTransport()
    {
        // A subprocess client (Claude Desktop, Glama's build check) talks over stdin/stdout,
        // which needs one long-lived transport registered as a service.
        var builder = Host.CreateApplicationBuilder();

        Program.ConfigureServices(builder, stdio: true);

        builder.Services.Should().Contain(d => d.ServiceType == typeof(ITransport));
    }

    [Fact]
    public void ConfigureServices_Default_RegistersNoStdioTransport()
    {
        // The HTTP host creates a transport per session, so a registered stdio transport
        // there would read the process's stdin alongside the web server.
        var builder = Host.CreateApplicationBuilder();

        Program.ConfigureServices(builder);

        builder.Services.Should().NotContain(d => d.ServiceType == typeof(ITransport));
    }
}
