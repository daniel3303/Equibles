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

    // The same statement and the same typed parameters the scan sent, planned the way Npgsql
    // plans them (at Bind, with the values), with the sequential scan made as expensive as
    // possible so only a predicate the planner cannot prove leaves the index unused.
    private async Task<List<string>> Explain(IEnumerable<CapturedCommand> commands)
    {
        var plans = new List<string>();
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using (var seqScanOff = new NpgsqlCommand("SET enable_seqscan = off;", connection))
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
