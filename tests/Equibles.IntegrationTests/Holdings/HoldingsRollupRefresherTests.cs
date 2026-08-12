using Equibles.Data;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.HostedService.Services;
using Equibles.IntegrationTests.Helpers;
using Microsoft.EntityFrameworkCore;

namespace Equibles.IntegrationTests.Holdings;

[Collection(ParadeDbCollection.Name)]
public class HoldingsRollupRefresherTests : IAsyncLifetime
{
    private static readonly DateOnly Prior = new(2024, 9, 30);
    private static readonly DateOnly Current = new(2024, 12, 31);

    private readonly ParadeDbFixture _fixture;

    public HoldingsRollupRefresherTests(ParadeDbFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync() => await _fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task MarkAumSnapshotsDirty_ReplacesClaimAndDirtiesSuccessor()
    {
        var activeClaim = DateTime.UtcNow.AddMinutes(5);
        await using (var seed = FreshContext())
        {
            seed.AddRange(
                new AumQuarterlySnapshot { ReportDate = Prior, DirtyAt = activeClaim },
                new AumQuarterlySnapshot { ReportDate = Current }
            );
            await seed.SaveChangesAsync();
        }

        var before = DateTime.UtcNow;
        await using (var write = FreshContext())
        {
            await HoldingsRollupRefresher.MarkAumSnapshotsDirty(
                write,
                [Prior],
                CancellationToken.None
            );
        }
        var after = DateTime.UtcNow;

        await using var read = FreshContext();
        var snapshots = await read.Set<AumQuarterlySnapshot>()
            .OrderBy(snapshot => snapshot.ReportDate)
            .ToListAsync();
        snapshots.Should().HaveCount(2);
        snapshots.Should().OnlyContain(snapshot => snapshot.DirtyAt != null);
        snapshots.Should().OnlyContain(snapshot => snapshot.DirtyAt >= before);
        snapshots.Should().OnlyContain(snapshot => snapshot.DirtyAt <= after);
    }

    [Fact]
    public async Task MarkAumSnapshotsDirty_PreservesFirstOrdinaryEventTimestamp()
    {
        var firstEvent = DateTime.UtcNow.AddMinutes(-10);
        await using (var seed = FreshContext())
        {
            seed.Add(new AumQuarterlySnapshot { ReportDate = Prior, DirtyAt = firstEvent });
            await seed.SaveChangesAsync();
        }

        await using (var write = FreshContext())
        {
            await HoldingsRollupRefresher.MarkAumSnapshotsDirty(
                write,
                [Prior],
                CancellationToken.None
            );
        }

        await using var read = FreshContext();
        var snapshot = await read.Set<AumQuarterlySnapshot>().SingleAsync();
        snapshot.DirtyAt.Should().BeCloseTo(firstEvent, TimeSpan.FromMilliseconds(1));
    }

    private EquiblesFinancialDbContext FreshContext() => _fixture.CreateDbContext();
}
