using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Repositories;
using Equibles.Data;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Equibles.UnitTests.CommonStocks;

/// <summary>
/// <c>LockIssuersForNoKeyUpdate</c> is one set-based statement, never a per-row reload, so a
/// batch writer can hold the directory-identity lock for milliseconds instead of minutes.
/// </summary>
public class EquityIssuerRepositoryLockIssuersForNoKeyUpdateTests
{
    private const string SourcePath =
        "src/Equibles.CommonStocks.Repositories/EquityIssuerRepository.cs";

    [Fact]
    public async Task NonRelationalProvider_IsANoOp()
    {
        await using var db = new EquiblesFinancialDbContext(
            new DbContextOptionsBuilder<EquiblesFinancialDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .EnableServiceProviderCaching(false)
                .Options,
            new IModuleConfiguration[] { new CommonStocksModuleConfiguration() }
        );

        var act = () => new EquityIssuerRepository(db).LockIssuersForNoKeyUpdate([Guid.NewGuid()]);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task RelationalProvider_WithoutATransaction_Throws()
    {
        await using var db = NpgsqlContext();

        var act = () => new EquityIssuerRepository(db).LockIssuersForNoKeyUpdate([Guid.NewGuid()]);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*transaction*");
    }

    [Fact]
    public async Task EmptyBatch_NeverTouchesTheDatabase()
    {
        await using var db = NpgsqlContext();

        var act = () => new EquityIssuerRepository(db).LockIssuersForNoKeyUpdate([]);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public void Statement_IsOneOrderedSetBasedNoKeyUpdateLock()
    {
        var source = File.ReadAllText(FindRepositoryPath(SourcePath));
        var start = source.IndexOf("Task LockIssuersForNoKeyUpdate(", StringComparison.Ordinal);
        start.Should().BeGreaterThan(0);
        var end = source.IndexOf("\n    public ", start, StringComparison.Ordinal);
        var body = source[start..end];

        body.Should().Contain("= ANY({ids})");
        body.Should().Contain("ORDER BY \\\"Id\\\" FOR NO KEY UPDATE");
        body.Should().NotContain("GetWithWriteLock");
        body.Should().NotContain("Reload");
    }

    private static EquiblesFinancialDbContext NpgsqlContext() =>
        new(
            new DbContextOptionsBuilder<EquiblesFinancialDbContext>()
                .UseNpgsql("Host=localhost;Database=none;Username=none;Password=none")
                .EnableServiceProviderCaching(false)
                .Options,
            new IModuleConfiguration[] { new CommonStocksModuleConfiguration() }
        );

    private static string FindRepositoryPath(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            var candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate))
                return candidate;
            directory = directory.Parent;
        }

        throw new FileNotFoundException(
            $"Could not locate {relativePath} above {AppContext.BaseDirectory}"
        );
    }
}
