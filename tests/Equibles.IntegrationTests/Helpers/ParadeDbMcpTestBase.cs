using System.Globalization;
using Equibles.Data;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Repositories;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Equibles.IntegrationTests.Helpers;

/// <summary>
/// Base class for MCP tool tests that hit the shared ParadeDB fixture. Resets the DB before
/// each test (xUnit constructs a new class instance per test, and <see cref="InitializeAsync"/>
/// runs before the test body), pins invariant culture so number/date formatting is
/// deterministic on every dev machine and CI runner, and exposes a fresh
/// <see cref="EquiblesDbContext"/> + helper <see cref="ErrorManager"/> wired to that same
/// context.
///
/// MCP tools format output with <c>:N0</c> / <c>:F2</c> which honour <c>CurrentCulture</c>;
/// without the culture pin a test asserting on <c>"10,000"</c> would fail in <c>fr-FR</c>
/// (which uses thin-space) or <c>pt-PT</c> (which uses dot). Culture is pinned in the
/// constructor — xUnit v2 does not flow <see cref="CultureInfo.CurrentCulture"/> from
/// <see cref="InitializeAsync"/> into the test body, so pinning there leaves the test
/// body running on the host's default culture and number assertions flake on non-en-US
/// machines.
/// </summary>
public abstract class ParadeDbMcpTestBase : IAsyncLifetime
{
    private readonly CultureInfo _previousCulture;
    protected ParadeDbFixture Fixture { get; }
    protected EquiblesDbContext DbContext { get; private set; }
    protected ErrorManager ErrorManager { get; private set; }

    protected ParadeDbMcpTestBase(ParadeDbFixture fixture)
    {
        Fixture = fixture;
        _previousCulture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
    }

    public virtual async Task InitializeAsync()
    {
        await Fixture.ResetAsync();
        DbContext = Fixture.CreateDbContext();
        ErrorManager = new ErrorManager(new ErrorRepository(DbContext));
    }

    public virtual Task DisposeAsync()
    {
        DbContext?.Dispose();
        CultureInfo.CurrentCulture = _previousCulture;
        return Task.CompletedTask;
    }

    protected static ILogger<T> NullLogger<T>() => Substitute.For<ILogger<T>>();
}
