using Equibles.Core.Configuration;
using Equibles.Worker;

namespace Equibles.UnitTests.Core;

public class SyncDateResolverTests {
    [Fact]
    public void Resolve_NonDefaultLatestDate_ReturnsNextDay() {
        var latest = new DateOnly(2025, 6, 14);

        var result = SyncDateResolver.Resolve(latest, new WorkerOptions());

        result.Should().Be(new DateOnly(2025, 6, 15));
    }

    [Fact]
    public void Resolve_DefaultLatestAndConfiguredMinSyncDate_ReturnsOperatorValue() {
        // Operators override the baked-in 2020-01-01 fallback by setting
        // WorkerOptions.MinSyncDate — typically to either backfill further back
        // (e.g. CFTC COT history from 1986) or to constrain a brand-new deployment
        // to a recent window so the first sync doesn't drag in a decade of data.
        //
        // The risk this test pins: a refactor that collapses the null-check
        // (`workerOptions.MinSyncDate.HasValue ? ... : DefaultMinDate`) into a
        // bare `?? DefaultMinDate` on the wrong side, or that swaps the two
        // branch bodies, would silently restart every empty-DB sync from
        // 2020-01-01 regardless of operator config. The non-default-latest leg
        // is covered above; the null-MinSyncDate leg is covered below; this
        // covers the "configured" leg of the empty-DB branch — the third and
        // final reachable branch in Resolve.
        //
        // Picking 2018-03-15 specifically — a date strictly distinct from both
        // DefaultMinDate (2020-01-01) and from a default(DateOnly) so the
        // wrong-branch regression can't pass by coincidence.
        var minSync = new DateTime(2018, 3, 15);
        var result = SyncDateResolver.Resolve(default, new WorkerOptions { MinSyncDate = minSync });

        result.Should().Be(new DateOnly(2018, 3, 15));
    }

    [Fact]
    public void Resolve_DefaultLatestAndNullMinSyncDate_ReturnsBakedDefaultMinDate() {
        // Every domain worker that resolves its sync window through this helper
        // (Yahoo prices, FRED, CFTC, FTD) depends on the empty-DB fallback when
        // the operator hasn't set WorkerOptions.MinSyncDate. The fallback is a
        // hard-coded `new DateOnly(2020, 1, 1)` literal — drop the constant or
        // flip the null-check inversion and a freshly-deployed instance would
        // either crash on `default(DateOnly)` (year 0001) or start importing
        // from a runtime "now", silently shortening the historical backfill.
        // Pin the literal so a regression that changes the year (or the
        // fallback path entirely) is caught at test time. The sibling [Fact]
        // already covers the non-default-latest branch — this one covers the
        // null-MinSyncDate leg of the "no data yet" branch.
        var result = SyncDateResolver.Resolve(default, new WorkerOptions { MinSyncDate = null });

        result.Should().Be(new DateOnly(2020, 1, 1));
    }
}
