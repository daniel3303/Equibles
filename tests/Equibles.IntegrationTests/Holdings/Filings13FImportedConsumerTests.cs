using Equibles.Holdings.Data.Models;
using Equibles.Holdings.HostedService.Consumers;
using Equibles.IntegrationTests.Helpers;
using Equibles.Messaging.Contracts.Holdings;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Equibles.IntegrationTests.Holdings;

/// <summary>
/// Contract for <see cref="Filings13FImportedConsumer"/>: marks the AUM
/// snapshot for the affected quarter dirty in a single atomic upsert. Brand-new
/// quarters land as a stub row with <c>DirtyAt</c> set; the drain worker fills
/// in the aggregates on its next cooldown-expired tick.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class Filings13FImportedConsumerTests : IAsyncLifetime
{
    private readonly ParadeDbFixture _fixture;
    private readonly List<Equibles.Data.EquiblesFinancialDbContext> _contexts = [];

    public Filings13FImportedConsumerTests(ParadeDbFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync() => await _fixture.ResetAsync();

    public Task DisposeAsync()
    {
        foreach (var ctx in _contexts)
            ctx.Dispose();
        return Task.CompletedTask;
    }

    private Equibles.Data.EquiblesFinancialDbContext FreshContext()
    {
        var ctx = _fixture.CreateDbContext();
        _contexts.Add(ctx);
        return ctx;
    }

    private static readonly DateOnly Q4 = new(2024, 12, 31);

    private IServiceScopeFactory BuildScopeFactory()
    {
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        scopeFactory.CreateScope().Returns(_ => CreateScopeFromFixture());
        return scopeFactory;
    }

    private IServiceScope CreateScopeFromFixture()
    {
        var ctx = FreshContext();
        var scope = Substitute.For<IServiceScope>();
        var provider = Substitute.For<IServiceProvider>();
        provider.GetService(typeof(Equibles.Data.EquiblesFinancialDbContext)).Returns(ctx);
        scope.ServiceProvider.Returns(provider);
        return scope;
    }

    private Filings13FImportedConsumer BuildConsumer() =>
        new(BuildScopeFactory(), NullLogger<Filings13FImportedConsumer>.Instance);

    private static ConsumeContext<Filings13FImported> Context(Filings13FImported message)
    {
        var ctx = Substitute.For<ConsumeContext<Filings13FImported>>();
        ctx.Message.Returns(message);
        ctx.CancellationToken.Returns(CancellationToken.None);
        return ctx;
    }

    [Fact]
    public async Task Consume_NoSnapshotRowYet_InsertsDirtyStubRow()
    {
        var before = DateTime.UtcNow;
        await BuildConsumer().Consume(Context(new Filings13FImported(Q4, FilingCount: 1)));
        var after = DateTime.UtcNow;

        await using var read = FreshContext();
        var aum = await read.Set<AumQuarterlySnapshot>().SingleAsync(s => s.ReportDate == Q4);
        aum.DirtyAt.Should().NotBeNull();
        aum.DirtyAt!.Value.Should().BeOnOrAfter(before).And.BeOnOrBefore(after);
        aum.TotalValue.Should().Be(0L, "brand-new quarter is a stub until the drain rebuilds");
        aum.FilerCount.Should().Be(0);
        aum.PositionCount.Should().Be(0);
    }

    [Fact]
    public async Task Consume_ExistingRowNotDirty_MarksDirtyAtWithoutTouchingAggregates()
    {
        var preComputedAt = DateTime.UtcNow.AddHours(-3);
        await using (var seed = FreshContext())
        {
            seed.Add(
                new AumQuarterlySnapshot
                {
                    ReportDate = Q4,
                    TotalValue = 1_000_000L,
                    FilerCount = 42,
                    PositionCount = 100,
                    StockCount = 10,
                    FilingCount = 5,
                    ComputedAt = preComputedAt,
                }
            );
            await seed.SaveChangesAsync();
        }

        var before = DateTime.UtcNow;
        await BuildConsumer().Consume(Context(new Filings13FImported(Q4, FilingCount: 1)));
        var after = DateTime.UtcNow;

        await using var read = FreshContext();
        var aum = await read.Set<AumQuarterlySnapshot>().SingleAsync(s => s.ReportDate == Q4);
        aum.DirtyAt.Should().NotBeNull();
        aum.DirtyAt!.Value.Should().BeOnOrAfter(before).And.BeOnOrBefore(after);
        aum.ComputedAt.Should()
            .BeCloseTo(preComputedAt, TimeSpan.FromSeconds(1), "aggregate path untouched");
        aum.TotalValue.Should().Be(1_000_000L, "no rebuild — aggregate values are preserved");
        aum.FilerCount.Should().Be(42);
    }

    [Fact]
    public async Task Consume_AlreadyDirty_PreservesOriginalDirtyAt()
    {
        var firstEventAt = DateTime.UtcNow.AddMinutes(-30);
        await using (var seed = FreshContext())
        {
            seed.Add(
                new AumQuarterlySnapshot
                {
                    ReportDate = Q4,
                    TotalValue = 1L,
                    DirtyAt = firstEventAt,
                }
            );
            await seed.SaveChangesAsync();
        }

        // Second event in the same wave — DirtyAt should not be reset to a
        // newer timestamp, so the cooldown is measured from the first event.
        await BuildConsumer().Consume(Context(new Filings13FImported(Q4, FilingCount: 1)));

        await using var read = FreshContext();
        var aum = await read.Set<AumQuarterlySnapshot>().SingleAsync(s => s.ReportDate == Q4);
        aum.DirtyAt.Should().NotBeNull();
        aum.DirtyAt!.Value.Should().BeCloseTo(firstEventAt, TimeSpan.FromSeconds(1));
    }
}
