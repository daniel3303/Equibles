using Equibles.Core.Configuration;
using Equibles.Worker;

namespace Equibles.UnitTests.Core;

/// <summary>
/// Contract: Resolve determines the start date for a sync operation.
/// When latestDateInDb is non-default, it returns latestDateInDb + 1 day.
/// DateOnly.MaxValue (9999-12-31) is a valid non-default date, but
/// AddDays(1) overflows to year 10000 — outside DateOnly's valid range.
/// The method should handle this boundary gracefully rather than throwing
/// an unhandled ArgumentOutOfRangeException.
/// </summary>
public class SyncDateResolverMaxValueOverflowTests
{
    [Fact(Skip = "GH-1878 — AddDays(1) overflows on DateOnly.MaxValue")]
    public void Resolve_LatestDateIsMaxValue_DoesNotThrow()
    {
        var act = () => SyncDateResolver.Resolve(DateOnly.MaxValue, new WorkerOptions());

        act.Should()
            .NotThrow(
                "Resolve should handle DateOnly.MaxValue gracefully — AddDays(1) overflows to year 10000"
            );
    }
}
