using Equibles.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace Equibles.IntegrationTests.Helpers;

internal sealed class IsolatedMigrationDatabase : IAsyncDisposable
{
    private readonly string _administrationConnection;
    private readonly string _database;
    public EquiblesFinancialDbContext Context { get; }
    public string ConnectionString { get; }

    private IsolatedMigrationDatabase(ParadeDbFixture fixture)
    {
        _administrationConnection = fixture.ConnectionString;
        _database = "migration_" + Guid.NewGuid().ToString("N");
        var connection = new NpgsqlConnectionStringBuilder(fixture.ConnectionString)
        {
            Database = _database,
            Pooling = false,
        };
        ConnectionString = connection.ConnectionString;
        Context = fixture.CreateDbContext(options => options.UseNpgsql(ConnectionString));
        Context.Database.SetCommandTimeout(TimeSpan.FromMinutes(5));
    }

    public static async Task<IsolatedMigrationDatabase> Create(
        ParadeDbFixture fixture,
        string beforeMigration
    )
    {
        var database = new IsolatedMigrationDatabase(fixture);
        await database.Administer($"CREATE DATABASE {database._database}");
        try
        {
            await database.Context.GetService<IMigrator>().MigrateAsync(beforeMigration);
            return database;
        }
        catch
        {
            await database.DisposeAsync();
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await Context.DisposeAsync();
        await Administer($"DROP DATABASE IF EXISTS {_database} WITH (FORCE)");
    }

    private async Task Administer(string sql)
    {
        await using var connection = new NpgsqlConnection(_administrationConnection);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection) { CommandTimeout = 60 };
        await command.ExecuteNonQueryAsync();
    }
}
