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
