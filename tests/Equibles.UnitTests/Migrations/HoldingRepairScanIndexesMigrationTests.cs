using Equibles.UnitTests.Holdings;

namespace Equibles.UnitTests.Migrations;

public class HoldingRepairScanIndexesMigrationTests
{
    private const string MigrationPath =
        "src/Equibles.Migrations/Migrations/20260915014158_AddHoldingRepairScanIndexes.cs";

    [Fact]
    public void Migration_IsConcurrentAndRetrySafe()
    {
        var migration = File.ReadAllText(FindRepositoryPath(MigrationPath));
        var downStart = migration.IndexOf("protected override void Down", StringComparison.Ordinal);
        var up = migration[..downStart];
        var down = migration[downStart..];

        up.Should()
            .ContainEquivalentOf(
                "NOT i.indisvalid",
                Exactly.Twice(),
                "only an interrupted build is dropped; a finished index survives a retry"
            );
        up.Should().NotContain("DROP INDEX CONCURRENTLY");
        up.Should().ContainEquivalentOf("CREATE INDEX CONCURRENTLY IF NOT EXISTS", Exactly.Twice());
        up.Should().ContainEquivalentOf("suppressTransaction: true", Exactly.Times(4));
        up.Should().NotContain("CreateIndex(", "the scaffolded form builds under a table lock");
        down.Should().ContainEquivalentOf("DROP INDEX CONCURRENTLY IF EXISTS", Exactly.Twice());
        down.Should().ContainEquivalentOf("suppressTransaction: true", Exactly.Twice());
    }

    [Fact]
    public void Migration_SpellsBothPredicatesExactlyAsTheModelDoes()
    {
        var up = File.ReadAllText(FindRepositoryPath(MigrationPath));

        Unescape(up).Should().Contain("WHERE " + HoldingsModuleRepairScanIndexTests.ImplausibleFilter + ";");
        Unescape(up).Should().Contain("WHERE " + HoldingsModuleRepairScanIndexTests.ImpossibleFilter + ";");
        Unescape(up).Should().Contain("ON \"InstitutionalHolding\" (\"EquityIssuerId\", \"Shares\")");
    }

    // The migration keeps one SQL statement per C# string concatenation; fold the source back
    // into the statement Postgres receives.
    private static string Unescape(string source) =>
        source.Replace("\\\"", "\"").Replace("\"\n                    + \"", "");

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
