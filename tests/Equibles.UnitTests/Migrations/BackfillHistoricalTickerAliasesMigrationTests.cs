using System.Text.RegularExpressions;
using FluentAssertions;

namespace Equibles.UnitTests.Migrations;

public partial class BackfillHistoricalTickerAliasesMigrationTests
{
    private static readonly IReadOnlyDictionary<string, string> ExpectedMappings =
        new Dictionary<string, string>
        {
            ["SNBR"] = "827187",
            ["GOCO"] = "1808220",
            ["SATS"] = "1415404",
            ["SSSS"] = "1509470",
            ["SKLZ"] = "1801661",
            ["SCVL"] = "895447",
            ["IAC"] = "1800227",
            ["NOTV"] = "720154",
            ["ATLN"] = "1605888",
            ["BK"] = "1390777",
            ["CGCT"] = "2049662",
            ["LOKV"] = "2048951",
            ["SGMO"] = "1001233",
            ["USEG"] = "101594",
            ["XWIN"] = "1473334",
            ["WTO"] = "1789299",
            ["AGH"] = "2009312",
            ["CGEH"] = "1009759",
            ["DEVS"] = "1854480",
            ["SUUN"] = "2011053",
            ["ZBAI"] = "1755058",
        };

    [Fact]
    public void Migration_BackfillsExactlyTheAuthoritativeHistoricalOwners()
    {
        var migration = ReadMigration();
        var up = migration[..migration.IndexOf("protected override void Down", StringComparison.Ordinal)];
        var mappings = MappingTupleRegex()
            .Matches(up)
            .ToDictionary(match => match.Groups["ticker"].Value, match => match.Groups["cik"].Value);

        mappings.Should().BeEquivalentTo(ExpectedMappings);
        up.Should().Contain("JOIN \"CommonStock\" AS stock");
        up.Should().Contain("regexp_replace(stock.\"Cik\", '^0+', '') = historical.\"Cik\"");
        up.Should().Contain("coalesce(live_stock.\"SecondaryTickers\", ARRAY[]::text[])");
        up.Should().Contain("coalesce(live_stock.\"ReferenceTickers\", ARRAY[]::text[])");
        up.Should().Contain("duplicate_owner.\"Id\" <> stock.\"Id\"");
        up.Should().Contain("ON CONFLICT DO NOTHING");
    }

    [Fact]
    public void Migration_RollbackRemovesExactlyItsDeterministicRows()
    {
        var migration = ReadMigration();
        var down = migration[migration.IndexOf("protected override void Down", StringComparison.Ordinal)..];
        var mappings = MappingTupleRegex()
            .Matches(down)
            .ToDictionary(match => match.Groups["ticker"].Value, match => match.Groups["cik"].Value);
        var ids = MigrationIdRegex().Matches(down).Select(match => match.Value).ToList();
        var expectedIds = Enumerable.Range(1, ExpectedMappings.Count)
            .Select(sequence => $"70700000-0000-4000-8000-{sequence:000000000000}");

        mappings.Should().BeEquivalentTo(ExpectedMappings);
        ids.Should().Equal(expectedIds);
        down.Should().Contain("DELETE FROM \"CommonStockTickerAlias\" AS target");
        down.Should().Contain("target.\"Id\" = historical.\"Id\"");
        down.Should().Contain("target.\"Ticker\" = historical.\"Ticker\"");
        down.Should().Contain("stock.\"Id\" = target.\"CommonStockId\"");
        down.Should().Contain("regexp_replace(stock.\"Cik\", '^0+', '') = historical.\"Cik\"");
    }

    private static string ReadMigration() =>
        File.ReadAllText(FindRepositoryPath(
            "src/Equibles.Migrations/Migrations/20260812061636_BackfillHistoricalTickerAliases.cs"
        ));

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

    [GeneratedRegex(
        @"\('70700000-0000-4000-8000-\d{12}'::uuid, '(?<ticker>[A-Z0-9.-]+)', '(?<cik>\d+)'\)"
    )]
    private static partial Regex MappingTupleRegex();

    [GeneratedRegex(@"70700000-0000-4000-8000-\d{12}")]
    private static partial Regex MigrationIdRegex();
}
