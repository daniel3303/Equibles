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

    // Identity discovery must preserve the bulk import progress.
    [Fact]
    public async Task Consume_QueuesSourceIdentities_AndLeavesRealRowsIntact()
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
                new HoldingsCusipRescanRepository(ctx),
                _signal,
                Substitute.For<ILogger<StockCusipChangedConsumer>>()
            );
            await sut.Consume(
                Context(new StockCusipChanged(Guid.NewGuid(), "AAPL", null, "037833100"))
            );
        }

        await using var verify = _fixture.CreateDbContext();
        var rows = await verify.Set<ProcessedDataSet>().Select(r => r.FileName).ToListAsync();
        rows.Should()
            .BeEquivalentTo("01mar2025-31may2025_form13f.zip", "01dec2025-28feb2026_form13f.zip");

        var request = await verify.Set<HoldingsCusipRescan>().SingleAsync();
        request.Cusip.Should().Be("037833100");
        request.CompletedAt.Should().BeNull();
        // Queuing must wake the Holdings worker now.
        var wait = _signal.WaitAsync(CancellationToken.None);
        (await Task.WhenAny(wait, Task.Delay(TimeSpan.FromSeconds(1))))
            .Should()
            .Be(wait, "the consumer must signal a rescan after queuing the sentinel");
    }

    // Pre-upgrade markers do not contain identities and cannot absorb new requests.
    [Fact]
    public async Task Consume_LegacyRescanAlreadyQueued_RetainsNewIdentityAndWakesWorker()
    {
        await using (var seed = _fixture.CreateDbContext())
        {
            seed.Set<ProcessedDataSet>()
                .Add(new ProcessedDataSet { FileName = ProcessedDataSet.RescanPendingFileName });
            await seed.SaveChangesAsync();
        }

        await using (var ctx = _fixture.CreateDbContext())
        {
            var sut = new StockCusipChangedConsumer(
                new HoldingsCusipRescanRepository(ctx),
                _signal,
                Substitute.For<ILogger<StockCusipChangedConsumer>>()
            );
            await sut.Consume(
                Context(new StockCusipChanged(Guid.NewGuid(), "MSFT", "abc", "594918104"))
            );
        }

        // Retain the new identity independently of an older global rescan.
        var wait = _signal.WaitAsync(CancellationToken.None);
        (await Task.WhenAny(wait, Task.Delay(TimeSpan.FromMilliseconds(300))))
            .Should()
            .Be(wait, "new identity work must wake the worker even if a legacy marker exists");

        await using var verify = _fixture.CreateDbContext();
        var rows = await verify.Set<ProcessedDataSet>().Select(r => r.FileName).ToListAsync();
        rows.Should().ContainSingle().Which.Should().Be(ProcessedDataSet.RescanPendingFileName);
        var request = await verify.Set<HoldingsCusipRescan>().SingleAsync();
        request.Cusip.Should().Be("594918104");
        request.PreviousCusip.Should().Be("abc");
    }
}
