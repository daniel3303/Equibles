using Equibles.Holdings.Data.Models;
using Equibles.Holdings.HostedService.Consumers;
using Equibles.Holdings.Repositories;
using Equibles.IntegrationTests.Helpers;
using Equibles.Messaging.Contracts.CommonStocks;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;
using Equibles.Holdings.HostedService;

namespace Equibles.IntegrationTests.Holdings;

/// <summary>
/// Adversarial sibling: the feature's pins cover "real rows + no guard" and
/// "guard only". The MIXED state — guard sentinel AND fresh real rows — is real
/// and untested: a prior CUSIP change invalidated + added the guard, the worker
/// re-imported quarterly sets, then another FTD-seeded CUSIP change arrives. A
/// second CUSIP change must re-invalidate the new real rows WITHOUT inserting a
/// duplicate guard (FileName has a unique index → duplicate insert throws and
/// permanently breaks the consumer).
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
    public async Task Consume_GuardAndFreshRealRowsBothPresent_ReInvalidatesWithoutDuplicatingGuard()
    {
        await using (var seed = _fixture.CreateDbContext())
        {
            seed.Set<ProcessedDataSet>()
                .AddRange(
                    new ProcessedDataSet { FileName = ProcessedDataSet.BackfillGuardFileName },
                    // Worker re-imported these after the previous invalidation.
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
            var sut = new StockCusipChangedConsumer(new ProcessedDataSetRepository(ctx), new HoldingsRescanSignal(), Substitute.For<ILogger<StockCusipChangedConsumer>>());
            // Must not throw a unique-constraint violation from a duplicate guard.
            await sut.Consume(
                Context(new StockCusipChanged(Guid.NewGuid(), "MSFT", "OLD", "594918104"))
            );
        }

        await using var verify = _fixture.CreateDbContext();
        var rows = await verify.Set<ProcessedDataSet>().Select(r => r.FileName).ToListAsync();
        rows.Should()
            .ContainSingle("real rows re-invalidated; exactly one guard, never duplicated")
            .Which.Should()
            .Be(ProcessedDataSet.BackfillGuardFileName);
    }
}
