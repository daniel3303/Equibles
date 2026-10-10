using System.Data.Common;
using Equibles.IntegrationTests.Helpers;
using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Data.Models;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using Xunit;

namespace Equibles.IntegrationTests.Sec;

/// <summary>
/// Proves the migrated fold index serves the respelling lookup: the planner must reach the tags
/// through IX_FinancialConcept_TagFold, never filter every concept of the taxonomy.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class FinancialConceptTagFoldIndexPlanTests(ParadeDbFixture fixture) : IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task FoldedTagLookup_UsesTheFoldIndex(bool withTaxonomy)
    {
        await using (var db = fixture.CreateDbContext())
        {
            db.AddRange(
                Enumerable
                    .Range(0, 20_000)
                    .Select(index => new FinancialConcept
                    {
                        Taxonomy = index % 2 == 0 ? FactTaxonomy.UsGaap : FactTaxonomy.Custom,
                        Tag = $"Concept_{index}Tag",
                    })
            );
            db.Add(new FinancialConcept { Taxonomy = FactTaxonomy.Custom, Tag = "Segment_Opex" });
            db.Add(new FinancialConcept { Taxonomy = FactTaxonomy.Custom, Tag = "SegmentOpex" });
            await db.SaveChangesAsync();
            await db.Database.ExecuteSqlRawAsync("ANALYZE \"FinancialConcept\"");
        }
        var capture = new CommandCapture();
        await using var context = fixture.CreateDbContext(options =>
            options.AddInterceptors(capture)
        );
        var folded = new List<string> { "segmentopex" };
        var taxonomies = new List<FactTaxonomy> { FactTaxonomy.Custom };

        var query = context
            .Set<FinancialConcept>()
            .Where(c => folded.Contains(c.Tag.Replace("_", "").ToLower()));
        if (withTaxonomy)
            query = query.Where(c => taxonomies.Contains(c.Taxonomy));
        var tags = await query.Select(c => c.Tag).ToListAsync();

        tags.Should().BeEquivalentTo("Segment_Opex", "SegmentOpex");
        // Literal arguments keep the expression matchable by a generic plan as well.
        capture.Commands.Single().Text.Should().Contain("replace(f.\"Tag\", '_', '')");
        var plan = await Explain(capture.Commands.Single());
        plan.Should().Contain("IX_FinancialConcept_TagFold").And.NotContain("Seq Scan");
    }

    private async Task<string> Explain(CapturedCommand command)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var explain = new NpgsqlCommand("EXPLAIN " + command.Text, connection);
        foreach (var parameter in command.Parameters)
            explain.Parameters.Add(parameter.Clone());
        await using var reader = await explain.ExecuteReaderAsync();
        var lines = new List<string>();
        while (await reader.ReadAsync())
            lines.Add(reader.GetString(0));
        return string.Join('\n', lines);
    }

    private sealed record CapturedCommand(string Text, List<NpgsqlParameter> Parameters);

    private sealed class CommandCapture : DbCommandInterceptor
    {
        public List<CapturedCommand> Commands { get; } = [];

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default
        )
        {
            Commands.Add(
                new CapturedCommand(
                    command.CommandText,
                    command.Parameters.Cast<NpgsqlParameter>().Select(p => p.Clone()).ToList()
                )
            );
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
