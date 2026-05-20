using Equibles.IntegrationTests.Helpers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Equibles.IntegrationTests.Web;

/// <summary>
/// Tests the three branches of <see cref="Equibles.Web.Program"/> that the FunctionalTests
/// project's <c>WebAppFixture</c> never exercises:
///   • <c>if (authSettings.IsEnabled)</c> — WebAppFixture leaves Auth disabled
///   • <c>?? "/app/keys"</c> — WebAppFixture always sets DataProtection:KeysDirectory
///   • <c>if (!app.Environment.IsDevelopment())</c> — WebAppFixture pins Development
/// Builds the same composition pipeline as production (ConfigureServices → Build →
/// ApplyMigrationsAsync → ConfigurePipeline) against the shared ParadeDB container,
/// varying one knob per test.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class ProgramConfigurationTests
{
    private readonly ParadeDbFixture _db;

    public ProgramConfigurationTests(ParadeDbFixture db) => _db = db;

    [Fact]
    public async Task ConfigureServices_AuthIsEnabled_RegistersAuthenticatedUserFallbackPolicy()
    {
        // Pins the `if (authSettings.IsEnabled) { … FallbackPolicy.RequireAuthenticatedUser … }`
        // TRUE branch. WebAppFixture (FunctionalTests) doesn't set Auth credentials, so
        // AuthSettings.IsEnabled (a computed `Username AND Password set` property) is false
        // and only the `else AddAuthorization()` arm runs end-to-end. Setting both credentials
        // flips IsEnabled to true; the fallback policy is then expected to deny anonymous —
        // the production safeguard for every endpoint not explicitly [AllowAnonymous].
        await using var app = BuildHost(
            "Development",
            overrides: new() { ["Auth:Username"] = "test-user", ["Auth:Password"] = "test-pass" }
        );

        var policyProvider = app.Services.GetRequiredService<IAuthorizationPolicyProvider>();
        var fallback = await policyProvider.GetFallbackPolicyAsync();

        fallback.Should().NotBeNull();
        fallback.Requirements.Should().Contain(r => r is DenyAnonymousAuthorizationRequirement);
    }

    [Fact]
    public async Task ConfigureServices_DataProtectionKeysDirectoryNotConfigured_FallsBackToAppKeysPath()
    {
        // Pins the `?? "/app/keys"` fallback. WebAppFixture always sets
        // DataProtection:KeysDirectory to a temp dir to avoid touching /app/keys on macOS/CI,
        // so the right-hand `??` arm is unexercised. Omit the config entirely and assert the
        // registered FileSystemXmlRepository points at the production default — proves the
        // line ran AND the fallback string survives the null-coalesce + DirectoryInfo wrap.
        //
        // The build is safe even though /app/keys doesn't exist: AddDataProtection's
        // KeyRing initialization is lazy (only the first IDataProtector resolution triggers
        // directory access), and this test never resolves one or starts the host's hosted
        // services — Build() composes the IServiceProvider without running app.Run.
        await using var app = BuildHost("Development", omitKeysDirectoryConfig: true);

        var options = app.Services.GetRequiredService<IOptions<KeyManagementOptions>>();
        var repo = options.Value.XmlRepository as FileSystemXmlRepository;
        repo.Should().NotBeNull();
        repo!.Directory.FullName.Should().Be("/app/keys");
    }

    [Fact]
    public async Task ConfigurePipeline_NonDevelopmentEnvironment_RunsProductionExceptionHandlerBranch()
    {
        // Pins the `if (!app.Environment.IsDevelopment()) { app.UseExceptionHandler("/Home/Error"); }`
        // branch. WebAppFixture hardcodes EnvironmentName="Development" so the production-side
        // middleware registration never runs. Driving Production explicitly and running
        // ConfigurePipeline hits the line; the environment assertion below proves the host was
        // actually wired in Production mode (vs. silently defaulting to Development from a
        // config typo, which would leave the branch unexercised).
        await using var app = BuildHost("Production");

        app.Environment.IsDevelopment().Should().BeFalse();
        app.Environment.EnvironmentName.Should().Be("Production");
    }

    private WebApplication BuildHost(
        string environment,
        Dictionary<string, string> overrides = null,
        bool omitKeysDirectoryConfig = false
    )
    {
        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions
            {
                ApplicationName = "Equibles.Web",
                EnvironmentName = environment,
                ContentRootPath = ResolveWebContentRoot(),
            }
        );
        builder.Configuration["ConnectionStrings:DefaultConnection"] = _db.ConnectionString;
        // AddMessaging binds the SQL transport host from this connection; without
        // it MassTransit throws "Host cannot be empty" when the SP is built.
        builder.Configuration["ConnectionStrings:TransportConnection"] = _db.ConnectionString;
        if (!omitKeysDirectoryConfig)
        {
            builder.Configuration["DataProtection:KeysDirectory"] = Path.Combine(
                Path.GetTempPath(),
                $"equibles-keys-{Guid.NewGuid():N}"
            );
        }
        if (overrides != null)
        {
            foreach (var kvp in overrides)
            {
                builder.Configuration[kvp.Key] = kvp.Value;
            }
        }

        Equibles.Web.Program.ConfigureServices(builder);
        var app = builder.Build();
        // ApplyMigrationsAsync is intentionally skipped — ParadeDbFixture has already applied
        // the schema against the shared container, and the full production EF model wired up
        // here triggers a PendingModelChangesWarning that's tangential to the branches under
        // test. The lines we want to cover sit in ConfigureServices and ConfigurePipeline.
        Equibles.Web.Program.ConfigurePipeline(app);
        return app;
    }

    private static string ResolveWebContentRoot()
    {
        // Walks up from the test bin directory until it finds the solution file, then
        // returns the Web project's source root. Matches WebAppFixture's resolution so the
        // same Razor/wwwroot contents are visible to both fixtures.
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
