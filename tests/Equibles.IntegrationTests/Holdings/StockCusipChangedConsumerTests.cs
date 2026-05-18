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

[Collection(ParadeDbCollection.Name)]
public class StockCusipChangedConsumerTests : IAsyncLifetime
{
    private readonly ParadeDbFixture _fixture;

    private readonly HoldingsRescanSignal _signal = new();

    public StockCusipChangedConsumerTests(ParadeDbFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync() => await _fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private static ConsumeContext<StockCusipChanged> Context(StockCusipChanged message)
    {
        var ctx = Substitute.For<ConsumeContext<StockCusipChanged>>();
        ctx.Message.Returns(message);
        ctx.CancellationToken.Returns(CancellationToken.None);
        return ctx;
    }

    // Contract: a CUSIP change clears the real quarterly ProcessedDataSet rows
    // (so HoldingsScraperWorker re-imports/backfills) while leaving a guard
    // sentinel (so the worker's empty-table backfill seeding doesn't re-skip
    // history).
    [Fact]
    public async Task Consume_InvalidatesRealDataSets_AndLeavesGuardSentinel()
    {
        await using (var seed = _fixture.CreateDbContext())
        {
            seed.Set<ProcessedDataSet>()
                .AddRange(
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
                _signal,
                Substitute.For<ILogger<StockCusipChangedConsumer>>()
            );
            await sut.Consume(
                Context(new StockCusipChanged(Guid.NewGuid(), "AAPL", null, "037833100"))
            );
        }

        await using var verify = _fixture.CreateDbContext();
        var rows = await verify.Set<ProcessedDataSet>().Select(r => r.FileName).ToListAsync();
        rows.Should().ContainSingle().Which.Should().Be(ProcessedDataSet.BackfillGuardFileName);

        // GH-852: invalidation must wake the Holdings worker now.
        var wait = _signal.WaitAsync(CancellationToken.None);
        (await Task.WhenAny(wait, Task.Delay(TimeSpan.FromSeconds(1))))
            .Should()
            .Be(wait, "the consumer must signal a rescan after invalidating ProcessedDataSet");
    }

    // Idempotent: once only the guard remains, a further event is a no-op
    // (the FTD cold-start seeding burst publishes one event per stock).
    [Fact]
    public async Task Consume_AlreadyInvalidated_IsNoOp()
    {
        await using (var seed = _fixture.CreateDbContext())
        {
            seed.Set<ProcessedDataSet>()
                .Add(new ProcessedDataSet { FileName = ProcessedDataSet.BackfillGuardFileName });
            await seed.SaveChangesAsync();
        }

        await using (var ctx = _fixture.CreateDbContext())
        {
            var sut = new StockCusipChangedConsumer(
                new ProcessedDataSetRepository(ctx),
                _signal,
                Substitute.For<ILogger<StockCusipChangedConsumer>>()
            );
            await sut.Consume(
                Context(new StockCusipChanged(Guid.NewGuid(), "MSFT", "abc", "594918104"))
            );
        }

        // No invalidation happened (already cleared) → no rescan signalled.
        var wait = _signal.WaitAsync(CancellationToken.None);
        (await Task.WhenAny(wait, Task.Delay(TimeSpan.FromMilliseconds(300))))
            .Should()
            .NotBe(wait, "an already-invalidated no-op must not trigger a rescan");

        await using var verify = _fixture.CreateDbContext();
        var rows = await verify.Set<ProcessedDataSet>().Select(r => r.FileName).ToListAsync();
        rows.Should().ContainSingle().Which.Should().Be(ProcessedDataSet.BackfillGuardFileName);
    }
}
