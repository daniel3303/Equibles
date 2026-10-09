using System.Data.Common;
using Equibles.CommonStocks.Data.Models;
using Equibles.Data;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.HostedService.Services;
using Equibles.IntegrationTests.Helpers;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using NSubstitute;
using Xunit;

namespace Equibles.IntegrationTests.Holdings;

/// <summary>
/// Proves the planner's choice, not the SQL text: the exact commands the two repair scans send,
/// parameters included, are re-run under EXPLAIN against the migrated schema and must be served by
/// their partial indexes. A partial index whose predicate the planner cannot prove from the query
/// is silently ignored, which is how a scan ships with every test green and still times out.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class HoldingRepairScanIndexPlanTests(ParadeDbFixture fixture) : IAsyncLifetime
{
    private const int FillerRows = 2_000;

    public Task InitializeAsync() => fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task ImplausibleDerivationScan_IsServedByItsPartialIndex()
    {
        await Seed();
        var capture = new CommandCapture();
        await using var context = fixture.CreateDbContext(options =>
            options.AddInterceptors(capture)
        );

        var rows = await HoldingValueFallbackRepairService
            .BuildImplausibleDerivationCandidateQuery(context)
            .ToListAsync();

        rows.Should().HaveCount(2);
        var commands = capture
            .Commands.Where(c => c.Text.Contains("\"InstitutionalHolding\""))
            .ToList();
        commands.Should().NotBeEmpty();
        foreach (var plan in await Explain(commands))
        {
            plan.Should()
                .Contain("IX_InstitutionalHolding_ImplausibleDerivationRepair")
                .And.NotContain("Seq Scan on \"InstitutionalHolding\"");
        }
    }

    [Fact]
    public async Task ImpossiblePositionScan_ReadsHoldingsThroughItsPartialIndexOnly()
    {
        await Seed();
        var capture = new CommandCapture();
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        scopeFactory
            .CreateScope()
            .Returns(_ =>
            {
                var context = fixture.CreateDbContext(options => options.AddInterceptors(capture));
                var provider = Substitute.For<IServiceProvider>();
                provider.GetService(typeof(EquiblesFinancialDbContext)).Returns(context);
                var scope = Substitute.For<IServiceScope>();
                scope.ServiceProvider.Returns(provider);
                return scope;
            });
        var service = new ImpossiblePositionRepairService(
            scopeFactory,
            NullLogger<ImpossiblePositionRepairService>.Instance
        );

        (await service.Repair(CancellationToken.None)).Should().Be(3);

        var batches = capture
            .Commands.Where(c =>
                c.Text.Contains("\"InstitutionalHolding\"") && c.Text.Contains("\"Shares\" >")
            )
            .ToList();
        batches.Should().HaveCount(2, "the micro-float sits below the floor and is asked apart");
        var plans = await Explain(batches);
        for (var i = 0; i < batches.Count; i++)
        {
            batches[i]
                .Text.Should()
                .NotContain("HoldingManagerEntry")
                .And.NotContain("\"EquityIssuer\"");
            plans[i].Should().NotContain("Seq Scan on \"InstitutionalHolding\"");
            var floor = batches[i].Parameters.Select(p => p.Value).OfType<long>().Single();
            if (floor >= ImpossiblePositionRepairService.CandidateSharesFloor)
            {
                plans[i].Should().Contain("IX_InstitutionalHolding_ImpossiblePositionRepair");
            }
            else
            {
                floor.Should().Be(600_000, "the micro-float is asked at its own bar");
            }
        }
    }

    [Fact]
    public async Task FiledReviseScan_IsServedByItsPartialIndex()
    {
        await Seed();
        await SeedRepairCandidates(filedPublishes: 2, unmarkedZeros: 0);
        var capture = new CommandCapture();
        await using var context = fixture.CreateDbContext(options =>
            options.AddInterceptors(capture)
        );

        var rows = await HoldingValueFallbackRepairService
            .BuildReviseCandidateQuery(context, Guid.Empty)
            .ToListAsync();

        rows.Should().HaveCount(2);
        var commands = capture
            .Commands.Where(c => c.Text.Contains("\"InstitutionalHolding\""))
            .ToList();
        commands.Should().NotBeEmpty();
        foreach (var plan in await Explain(commands))
        {
            plan.Should()
                .Contain("IX_InstitutionalHolding_FiledReviseRepair")
                .And.NotContain("Seq Scan on \"InstitutionalHolding\"");
        }
    }

    [Fact]
    public async Task UnmarkedZeroScan_IsServedByItsPartialIndex()
    {
        await Seed();
        await SeedRepairCandidates(filedPublishes: 0, unmarkedZeros: 2);
        var capture = new CommandCapture();
        await using var context = fixture.CreateDbContext(options =>
            options.AddInterceptors(capture)
        );

        var rows = await HoldingValueFallbackRepairService
            .BuildUnmarkedZeroCandidateQuery(context)
            .ToListAsync();

        rows.Should().HaveCount(2);
        var commands = capture
            .Commands.Where(c => c.Text.Contains("\"InstitutionalHolding\""))
            .ToList();
        commands.Should().NotBeEmpty();
        foreach (var plan in await Explain(commands))
        {
            plan.Should()
                .Contain("IX_InstitutionalHolding_UnmarkedZeroRepair")
                .And.NotContain("Seq Scan on \"InstitutionalHolding\"");
        }
    }

    // The quarter rebuild's reads must stay index-only: the market aggregate counts distinct
    // accessions, which only the quarter index covers, and a heap fetch per position costs
    // gigabytes per quarter in production. Many filers and stocks on one date make a skip scan
    // of the holder- or stock-leading indexes visibly dearer than the quarter's contiguous range.
    [Fact]
    public async Task QuarterRebuildReads_AreServedByTheQuarterIndex()
    {
        var reportDate = new DateOnly(2024, 6, 30);
        await SeedQuarter(reportDate, holders: 400, stocks: 400);
        var capture = new CommandCapture();
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        scopeFactory
            .CreateScope()
            .Returns(_ =>
            {
                var context = fixture.CreateDbContext(options => options.AddInterceptors(capture));
                var provider = Substitute.For<IServiceProvider>();
                provider.GetService(typeof(EquiblesFinancialDbContext)).Returns(context);
                var scope = Substitute.For<IServiceScope>();
                scope.ServiceProvider.Returns(provider);
                return scope;
            });
        var service = new HoldingsAggregateRefreshService(
            scopeFactory,
            NullLogger<HoldingsAggregateRefreshService>.Instance
        );

        await service.RebuildQuarterAsync(reportDate, CancellationToken.None);

        var quarterReads = capture
            .Commands.Where(c =>
                c.Text.Contains("FROM \"InstitutionalHolding\"")
                && c.Text.Contains("\"ReportDate\" =")
            )
            .ToList();
        var marketAggregates = quarterReads
            .Where(c => c.Text.Contains("\"AccessionNumber\"") && c.Text.Contains("count(DISTINCT"))
            .ToList();
        marketAggregates.Should().ContainSingle();
        foreach (var plan in await Explain(marketAggregates))
        {
            plan.Should()
                .Contain("Index Only Scan using \"IX_InstitutionalHolding_QuarterRebuild\"")
                .And.NotContain("Seq Scan on \"InstitutionalHolding\"");
        }
        var groupedReads = quarterReads
            .Where(c => c.Text.Contains("GROUP BY") && c.Text.Contains("sum("))
            .ToList();
        groupedReads.Should().NotBeEmpty();
        foreach (var plan in await Explain(groupedReads))
        {
            plan.Should()
                .Contain("Index Only Scan using")
                .And.NotContain("Seq Scan on \"InstitutionalHolding\"");
        }
    }

    // One trustworthy 200M-share issuer with two thousand ordinary positions that sit inside both
    // indexes without matching either scan, two positions bigger than the issuer, and two whose
    // derived value implies a per-share price in the billions; plus a 300k-share micro-float whose
    // bar is below the scan's floor, so it is asked in its own batch at its true bar and its one
    // position above that bar is withdrawn too.
    private async Task Seed()
    {
        await using var context = fixture.CreateDbContext();
        EquityIssuer issuer = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "NAAS",
            Name: "Issuer",
            MarketCapitalization: 5_000_000_000,
            SharesOutStanding: 200_000_000
        );
        var holder = new InstitutionalHolder
        {
            Id = Guid.NewGuid(),
            Cik = "0000000001",
            Name = "Filer",
        };
        EquityIssuer microFloat = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "TINY",
            Name: "Micro-float",
            MarketCapitalization: 3_000_000,
            SharesOutStanding: 300_000
        );
        context.Set<EquityIssuer>().AddRange(issuer, microFloat);
        context.Set<InstitutionalHolder>().Add(holder);
        context
            .Set<InstitutionalHolding>()
            .Add(NewHolding(microFloat, holder, 0, shares: 700_000, value: 7_000_000));
        // One filer, one report date per row: the row identity index is (issuer, holder, date, ...).
        var row = 0;
        for (var i = 0; i < FillerRows; i++)
        {
            context
                .Set<InstitutionalHolding>()
                .Add(NewHolding(issuer, holder, row++, shares: 2_000_000, value: 50_000_000));
        }
        for (var i = 0; i < 2; i++)
        {
            context
                .Set<InstitutionalHolding>()
                .Add(
                    NewHolding(
                        issuer,
                        holder,
                        row++,
                        shares: 32_098_694_296,
                        value: 100_800_000_000
                    )
                );
            context
                .Set<InstitutionalHolding>()
                .Add(
                    NewHolding(issuer, holder, row++, shares: 1_000, value: 5_000_000_000_000_000)
                );
        }
        await context.SaveChangesAsync();
        await context.Database.ExecuteSqlRawAsync("ANALYZE \"InstitutionalHolding\";");
    }

    private static InstitutionalHolding NewHolding(
        EquityIssuer issuer,
        InstitutionalHolder holder,
        int row,
        long shares,
        long value
    ) =>
        new()
        {
            Id = Guid.NewGuid(),
            EquityIssuerId = issuer.Id,
            InstitutionalHolderId = holder.Id,
            ReportDate = new DateOnly(2015, 1, 1).AddDays(row),
            FilingDate = new DateOnly(2015, 1, 1).AddDays(row + 40),
            Shares = shares,
            Value = value,
            ShareType = ShareType.Shares,
            InvestmentDiscretion = InvestmentDiscretion.Sole,
            AccessionNumber = Guid.NewGuid().ToString()[..20],
        };

    // Filed publishes the revise phase re-examines (Filed, never ladder-stamped, still serving
    // the filed figure) and abandoned zeros with no filed figure for the unmarked-zero stamp,
    // on the seeded issuer and filer so neither other scan's expectations move.
    private async Task SeedRepairCandidates(int filedPublishes, int unmarkedZeros)
    {
        await using var context = fixture.CreateDbContext();
        var issuer = await context.Set<EquityIssuer>().OrderBy(i => i.Name).FirstAsync();
        var holder = await context.Set<InstitutionalHolder>().SingleAsync();
        var row = 10_000;
        for (var i = 0; i < filedPublishes; i++)
        {
            var holding = NewHolding(issuer, holder, row++, shares: 1_000, value: 50_000);
            holding.FiledValue = 50_000;
            holding.ValueSource = ValueSource.Filed;
            context.Set<InstitutionalHolding>().Add(holding);
        }
        for (var i = 0; i < unmarkedZeros; i++)
        {
            var holding = NewHolding(issuer, holder, row++, shares: 1_000, value: 0);
            holding.ValueRetryCount = 2;
            context.Set<InstitutionalHolding>().Add(holding);
        }
        await context.SaveChangesAsync();
        await context.Database.ExecuteSqlRawAsync("ANALYZE \"InstitutionalHolding\";");
    }

    // Five consecutive quarters held by many filers across many stocks, inserted interleaved so
    // a quarter's positions are scattered across the heap as in production: each filer reports
    // three stocks per quarter under one accession, so the target quarter is wide on both group
    // keys and a heap fetch per position is the cost the covering index exists to avoid. VACUUM
    // sets the visibility map, without which the planner prices every index-only scan as a heap
    // fetch per row.
    private async Task SeedQuarter(DateOnly reportDate, int holders, int stocks)
    {
        await using var context = fixture.CreateDbContext();
        var issuers = Enumerable
            .Range(0, stocks)
            .Select(i =>
                Equibles.TestSupport.EquityIssuerSeed.Create(
                    Ticker: $"S{i:D4}",
                    Name: $"Stock {i:D4}",
                    MarketCapitalization: 1_000_000_000,
                    SharesOutStanding: 100_000_000
                )
            )
            .ToList();
        context.Set<EquityIssuer>().AddRange(issuers);
        var quarters = Enumerable.Range(-2, 5).Select(q => reportDate.AddMonths(3 * q)).ToList();
        for (var h = 0; h < holders; h++)
        {
            var holder = new InstitutionalHolder
            {
                Id = Guid.NewGuid(),
                Cik = $"{h + 1:D10}",
                Name = $"Filer {h:D4}",
            };
            context.Set<InstitutionalHolder>().Add(holder);
            for (var q = 0; q < quarters.Count; q++)
            {
                var accession = $"{h + 1:D10}-{q:D2}-000001";
                for (var k = 0; k < 3; k++)
                {
                    context
                        .Set<InstitutionalHolding>()
                        .Add(
                            new InstitutionalHolding
                            {
                                Id = Guid.NewGuid(),
                                EquityIssuerId = issuers[(h * 3 + k) % stocks].Id,
                                InstitutionalHolderId = holder.Id,
                                ReportDate = quarters[q],
                                FilingDate = quarters[q].AddDays(45),
                                Shares = 1_000,
                                Value = 50_000,
                                ShareType = ShareType.Shares,
                                InvestmentDiscretion = InvestmentDiscretion.Sole,
                                AccessionNumber = accession,
                            }
                        );
                }
            }
        }
        await context.SaveChangesAsync();
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        // Testcontainers runs Postgres with synchronous_commit off, so the seed's commit record
        // is still unflushed when VACUUM runs and its tuples cannot be hinted committed, which
        // leaves every page not all-visible; the checkpoint flushes it first.
        await using (var checkpoint = new NpgsqlCommand("CHECKPOINT;", connection))
            await checkpoint.ExecuteNonQueryAsync();
        await using var vacuum = new NpgsqlCommand(
            "VACUUM (ANALYZE) \"InstitutionalHolding\";",
            connection
        );
        await vacuum.ExecuteNonQueryAsync();
    }

    // The same statement and the same typed parameters the scan sent, planned the way Npgsql
    // plans them (at Bind, with the values), with sequential and bitmap heap scans disabled so
    // only an index the planner cannot use for the statement leaves it unused.
    private async Task<List<string>> Explain(IEnumerable<CapturedCommand> commands)
    {
        var plans = new List<string>();
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using (
            var seqScanOff = new NpgsqlCommand(
                "SET enable_seqscan = off; SET enable_bitmapscan = off;",
                connection
            )
        )
        {
            await seqScanOff.ExecuteNonQueryAsync();
        }
        foreach (var command in commands)
        {
            await using var explain = new NpgsqlCommand("EXPLAIN " + command.Text, connection);
            foreach (var parameter in command.Parameters)
            {
                explain.Parameters.Add(parameter.Clone());
            }
            await using var reader = await explain.ExecuteReaderAsync();
            var lines = new List<string>();
            while (await reader.ReadAsync())
            {
                lines.Add(reader.GetString(0));
            }
            plans.Add(string.Join('\n', lines));
        }
        return plans;
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
