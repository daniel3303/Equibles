using Testcontainers.PostgreSql;
using Xunit;

namespace Equibles.Migrations.IntegrationTests;

public class HalfvecDatabaseFixture : IAsyncLifetime
{
    public PostgreSqlContainer Database { get; } =
        new PostgreSqlBuilder("paradedb/paradedb:0.24.0-pg18").Build();

    public async Task InitializeAsync() => await Database.StartAsync();

    public async Task DisposeAsync() => await Database.DisposeAsync();
}
