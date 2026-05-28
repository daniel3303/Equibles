using Equibles.Data;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.HostedService;
using Equibles.Holdings.HostedService.Services;
using Equibles.IntegrationTests.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Equibles.IntegrationTests.Holdings;

/// <summary>
/// Per-entry error isolation in <see cref="AumSnapshotDrainWorker.DrainOnce"/>.
/// The worker's XML doc promises a failed rebuild "will retry on next tick" —
/// that only holds if the failed entry's DirtyAt survives AND the per-entry
/// catch lets the loop continue so sibling dirty entries still drain in the
/// same tick.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class AumSnapshotDrainWorkerErrorIsolationTests : IAsyncLifetime
{
    private static readonly DateOnly FailingDate = new(2024, 9, 30);
    private static readonly DateOnly SucceedingDate = new(2024, 12, 31);

    private readonly ParadeDbFixture _fixture;
    private readonly List<EquiblesFinancialDbContext> _contexts = [];

    public AumSnapshotDrainWorkerErrorIsolationTests(ParadeDbFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync() => await _fixture.ResetAsync();

    public Task DisposeAsync()
    {
        foreach (var ctx in _contexts)
            ctx.Dispose();
        return Task.CompletedTask;
    }

    private EquiblesFinancialDbContext FreshContext()
    {
        var ctx = _fixture.CreateDbContext();
        _contexts.Add(ctx);
        return ctx;
    }

    private IServiceScopeFactory BuildScopeFactory()
    {
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        scopeFactory
            .CreateScope()
            .Returns(_ =>
            {
                var ctx = FreshContext();
                var scope = Substitute.For<IServiceScope>();
                var provider = Substitute.For<IServiceProvider>();
                provider.GetService(typeof(EquiblesFinancialDbContext)).Returns(ctx);
                scope.ServiceProvider.Returns(provider);
                return scope;
            });
        return scopeFactory;
    }

    [Fact]
    public async Task DrainOnce_OneEntryRebuildThrows_SiblingDrainsAndFailedKeepsDirtyAt()
    {
        var dirtyAt = DateTime.UtcNow.AddHours(-2);

        await using (var seed = FreshContext())
        {
            seed.AddRange(
                new AumQuarterlySnapshot
                {
                    ReportDate = FailingDate,
                    TotalValue = 111L,
                    DirtyAt = dirtyAt,
                },
                new AumQuarterlySnapshot
                {
                    ReportDate = SucceedingDate,
                    TotalValue = 222L,
                    DirtyAt = dirtyAt,
                }
            );
            await seed.SaveChangesAsync();
        }

        var scopeFactory = BuildScopeFactory();
        var refresh = new SelectiveThrowRefreshService(
            scopeFactory,
            NullLogger<HoldingsAggregateRefreshService>.Instance,
            throwForDate: FailingDate
        );
        var worker = new ZeroCooldownDrainWorker(
            scopeFactory,
            refresh,
            NullLogger<AumSnapshotDrainWorker>.Instance
        );

        await worker.DrainOnce(CancellationToken.None);

        await using var read = FreshContext();
        var failed = await read.Set<AumQuarterlySnapshot>()
            .SingleAsync(s => s.ReportDate == FailingDate);
        var succeeded = await read.Set<AumQuarterlySnapshot>()
            .SingleAsync(s => s.ReportDate == SucceedingDate);

        failed
            .DirtyAt.Should()
            .NotBeNull("rebuild threw — DirtyAt must survive so the next tick retries this entry")
            .And.Subject.As<DateTime?>()
            .Value.Should()
            .BeCloseTo(dirtyAt, TimeSpan.FromSeconds(1));
        succeeded
            .DirtyAt.Should()
            .BeNull("sibling entry's rebuild succeeded — per-entry catch must not stop the loop");
    }

    private sealed class ZeroCooldownDrainWorker : AumSnapshotDrainWorker
    {
        public ZeroCooldownDrainWorker(
            IServiceScopeFactory scopeFactory,
            HoldingsAggregateRefreshService refreshService,
            ILogger<AumSnapshotDrainWorker> logger
        )
            : base(scopeFactory, refreshService, logger) { }

        protected override TimeSpan StartupDelay => TimeSpan.Zero;
        protected override TimeSpan TickInterval => TimeSpan.FromMilliseconds(1);
        protected override TimeSpan Cooldown => TimeSpan.Zero;
    }

    private sealed class SelectiveThrowRefreshService : HoldingsAggregateRefreshService
    {
        private readonly DateOnly _throwForDate;

        public SelectiveThrowRefreshService(
            IServiceScopeFactory scopeFactory,
            ILogger<HoldingsAggregateRefreshService> logger,
            DateOnly throwForDate
        )
            : base(scopeFactory, logger)
        {
            _throwForDate = throwForDate;
        }

        public override Task RebuildQuarterAsync(
            DateOnly reportDate,
            CancellationToken cancellationToken
        )
        {
            if (reportDate == _throwForDate)
                throw new InvalidOperationException("simulated rebuild failure");
            return Task.CompletedTask;
        }
    }
}
