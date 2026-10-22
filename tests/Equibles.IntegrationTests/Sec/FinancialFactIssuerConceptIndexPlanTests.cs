using System.Data.Common;
using Equibles.CommonStocks.Data.Models;
using Equibles.IntegrationTests.Helpers;
using Equibles.Sec.Data.Models;
using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Data.Models;
using Equibles.Sec.FinancialFacts.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using Xunit;

namespace Equibles.IntegrationTests.Sec;

/// <summary>
/// Proves the migrated covering index serves an issuer-batch fact read index-only: on a table far
/// larger than memory every heap fetch is a random read, so the read must never visit the heap.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class FinancialFactIssuerConceptIndexPlanTests(ParadeDbFixture fixture) : IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task IssuerBatchFactRead_ScansTheCoveringIndexOnly()
    {
        var issuers = Enumerable
            .Range(0, 400)
            .Select(index => new EquityIssuer { Name = $"Issuer {index}" })
            .ToList();
        var concepts = Enumerable
            .Range(0, 10)
            .Select(index => new FinancialConcept
            {
                Taxonomy = FactTaxonomy.UsGaap,
                Tag = $"Concept{index}",
            })
            .ToList();
        await using (var db = fixture.CreateDbContext())
        {
            db.AddRange(issuers);
            db.AddRange(concepts);
            db.AddRange(
                // Filed quarter by quarter, as on the live table, so one issuer's facts spread
                // across the heap instead of sitting on a few pages.
                from quarter in Enumerable.Range(0, 24)
                from issuer in issuers
                from concept in concepts
                select new FinancialFact
                {
                    EquityIssuerId = issuer.Id,
                    FinancialConceptId = concept.Id,
                    Unit = "USD",
                    Value = quarter,
                    PeriodType = FactPeriodType.Duration,
                    PeriodStart = new DateOnly(2021, 1, 1).AddMonths(3 * quarter),
                    PeriodEnd = new DateOnly(2021, 3, 31).AddMonths(3 * quarter),
                    FiscalYear = 2021 + quarter / 4,
                    FiscalPeriod = SecFiscalPeriod.FullYear,
                    Form = DocumentType.TenQ,
                    FiledDate = new DateOnly(2021, 5, 1).AddMonths(3 * quarter),
                    AccessionNumber = $"{issuer.Name}-{quarter}",
                    DimensionsKey = quarter % 3 == 0 ? "segment" : "",
                }
            );
            await db.SaveChangesAsync();
        }
        await using (var connection = new NpgsqlConnection(fixture.ConnectionString))
        {
            await connection.OpenAsync();
            // Index-only scans need the visibility map a vacuum sets, as on the settled table.
            // Testcontainers runs Postgres with synchronous_commit off, so the checkpoint flushes
            // the seed's commit first or VACUUM cannot mark its pages all-visible.
            await using var checkpoint = new NpgsqlCommand("CHECKPOINT;", connection);
            await checkpoint.ExecuteNonQueryAsync();
            await using var vacuum = new NpgsqlCommand(
                "VACUUM (ANALYZE) \"FinancialFact\";",
                connection
            );
            await vacuum.ExecuteNonQueryAsync();
        }
        var capture = new CommandCapture();
        await using var context = fixture.CreateDbContext(options =>
            options.AddInterceptors(capture)
        );
        var issuerIds = issuers.Take(5).Select(issuer => issuer.Id).ToList();
        var conceptIds = concepts.Take(3).Select(concept => concept.Id).ToList();
        var floor = new DateOnly(2023, 1, 1);

        // The valuation universe's shape: an issuer batch, a concept list and a period floor.
        var facts = await new FinancialFactRepository(context)
            .GetConsolidatedByIssuerIds(issuerIds)
            .Where(f => conceptIds.Contains(f.FinancialConceptId) && f.PeriodEnd >= floor)
            .Select(f => new
            {
                f.EquityIssuerId,
                f.FinancialConceptId,
                f.PeriodStart,
                f.PeriodEnd,
                f.Value,
                f.Unit,
                f.FiledDate,
                f.Form,
            })
            .ToListAsync();

        facts.Should().NotBeEmpty();
        var plan = await Explain(capture.Commands.Last());
        plan.Should()
            .Contain("Index Only Scan using \"IX_FinancialFact_IssuerConceptPeriod\"")
            .And.NotContain("Seq Scan on \"FinancialFact\"");
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
