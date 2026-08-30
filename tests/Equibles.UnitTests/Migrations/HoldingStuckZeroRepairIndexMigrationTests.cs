namespace Equibles.UnitTests.Migrations;

public class HoldingStuckZeroRepairIndexMigrationTests
{
    private const string MigrationPath =
        "src/Equibles.Migrations/Migrations/20260830153706_AddHoldingStuckZeroRepairIndex.cs";

    [Fact]
    public void Migration_IsConcurrentAndRetrySafe()
    {
        var migration = File.ReadAllText(FindRepositoryPath(MigrationPath));
        var up = migration[..migration.IndexOf("protected override void Down", StringComparison.Ordinal)];
        var down = migration[migration.IndexOf("protected override void Down", StringComparison.Ordinal)..];

        up.Should().Contain("DROP INDEX CONCURRENTLY IF EXISTS");
        up.Should().Contain("CREATE INDEX CONCURRENTLY IF NOT EXISTS");
        up.Should().ContainEquivalentOf("suppressTransaction: true", Exactly.Twice());
        down.Should().Contain("DROP INDEX CONCURRENTLY IF EXISTS");
        down.Should().Contain("suppressTransaction: true");
    }

    private static string FindRepositoryPath(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            var candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException(
            $"Could not locate {relativePath} above {AppContext.BaseDirectory}"
        );
    }
}
