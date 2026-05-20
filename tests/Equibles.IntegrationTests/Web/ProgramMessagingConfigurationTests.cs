using Equibles.IntegrationTests.Helpers;
using MassTransit;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Equibles.IntegrationTests.Web;

/// <summary>
/// Pins the line that adds MassTransit to the web host. The web subscribes to
/// events published by the worker (live ScraperActivity feed and any future
/// cross-host event), so omitting <c>AddMessaging</c> silently breaks every
/// consumer the web tries to wire — a regression we want a test to catch.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class ProgramMessagingConfigurationTests
{
    private readonly ParadeDbFixture _db;

    public ProgramMessagingConfigurationTests(ParadeDbFixture db) => _db = db;

    [Fact]
    public async Task ConfigureServices_RegistersMassTransitBusAndTransportOptions()
    {
        const string transport =
            "Host=localhost;Database=equibles;Username=postgres;Password=postgres";

        await using var app = BuildHost(transport);

        app.Services.GetService<IBus>()
            .Should()
            .NotBeNull("AddMessaging is what brings the bus into the container");
        app.Services.GetRequiredService<IOptions<SqlTransportOptions>>()
            .Value.ConnectionString.Should()
            .Be(transport, "AddMessaging binds the transport connection string");
    }

    private WebApplication BuildHost(string transportConnection)
    {
        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions
            {
                ApplicationName = "Equibles.Web",
                EnvironmentName = "Development",
                ContentRootPath = ResolveWebContentRoot(),
            }
        );
        builder.Configuration["ConnectionStrings:DefaultConnection"] = _db.ConnectionString;
        builder.Configuration["ConnectionStrings:TransportConnection"] = transportConnection;
        builder.Configuration["DataProtection:KeysDirectory"] = Path.Combine(
            Path.GetTempPath(),
            $"equibles-msg-keys-{Guid.NewGuid():N}"
        );

        Equibles.Web.Program.ConfigureServices(builder);
        return builder.Build();
    }

    private static string ResolveWebContentRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Equibles.sln")))
        {
            dir = dir.Parent;
        }
        if (dir is null)
        {
            throw new InvalidOperationException(
                "Could not locate Equibles.sln from test bin directory — cannot resolve ContentRootPath."
            );
        }
        return Path.Combine(dir.FullName, "src", "Equibles.Web");
    }
}
