using Equibles.Holdings.Data.Models;
using Equibles.Holdings.HostedService;
using Equibles.Holdings.HostedService.Consumers;
using Equibles.Holdings.Repositories;
using Equibles.IntegrationTests.Helpers;
using Equibles.Messaging.Contracts.CommonStocks;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Equibles.IntegrationTests.Holdings;

/// <summary>
/// Adversarial sibling: the feature's pins cover "real rows only" and
/// "sentinel only". The MIXED state — backfill guard AND fresh real rows — is
/// the steady state after a completed rescan: the worker applied a previous
/// sentinel (leaving the guard), re-imported quarterly sets, and then another
/// FTD-seeded CUSIP change arrives. The event must queue a new rescan sentinel
/// WITHOUT touching the guard, the real rows, or throwing (FileName has a
/// unique index → a duplicate guard insert would permanently break the
/// consumer).
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class StockCusipChangedConsumerMixedStateTests : IAsyncLifetime
{
    private readonly ParadeDbFixture _fixture;

    public StockCusipChangedConsumerMixedStateTests(ParadeDbFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync() => await _fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private static ConsumeContext<StockCusipChanged> Context(StockCusipChanged message)
    {
        var ctx = Substitute.For<ConsumeContext<StockCusipChanged>>();
        ctx.Message.Returns(message);
        ctx.CancellationToken.Returns(CancellationToken.None);
        return ctx;
    }

    [Fact]
    public async Task Consume_GuardAndFreshRealRowsBothPresent_QueuesSentinelAndTouchesNothingElse()
    {
        await using (var seed = _fixture.CreateDbContext())
        {
            seed.Set<ProcessedDataSet>()
                .AddRange(
                    new ProcessedDataSet { FileName = ProcessedDataSet.BackfillGuardFileName },
                    // Worker re-imported these after the previous rescan.
                    new ProcessedDataSet
                    {
                        FileName = "01mar2025-31may2025_form13f.zip",
                        SubmissionCount = 7987,
                    },
                    new ProcessedDataSet
                    {
                        FileName = "01dec2025-28feb2026_form13f.zip",
                        SubmissionCount = 8943,
                    }
                );
            await seed.SaveChangesAsync();
        }

        await using (var ctx = _fixture.CreateDbContext())
        {
            var sut = new StockCusipChangedConsumer(
                new ProcessedDataSetRepository(ctx),
                new HoldingsRescanSignal(),
                Substitute.For<ILogger<StockCusipChangedConsumer>>()
            );
            // Must not throw a unique-constraint violation from a duplicate guard.
            await sut.Consume(
                Context(new StockCusipChanged(Guid.NewGuid(), "MSFT", "OLD", "594918104"))
            );
        }

        await using var verify = _fixture.CreateDbContext();
        var rows = await verify.Set<ProcessedDataSet>().Select(r => r.FileName).ToListAsync();
        rows.Should()
            .BeEquivalentTo(
                ProcessedDataSet.BackfillGuardFileName,
                "01mar2025-31may2025_form13f.zip",
                "01dec2025-28feb2026_form13f.zip",
                ProcessedDataSet.RescanPendingFileName
            );
    }
}
