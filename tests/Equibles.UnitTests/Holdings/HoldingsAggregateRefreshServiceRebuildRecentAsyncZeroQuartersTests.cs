using Equibles.Holdings.HostedService.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Equibles.UnitTests.Holdings;

/// <summary>
/// Adversarial Lane A. `RebuildRecentAsync(int quarters, …)` declares a
/// positive-count parameter; the contract (encoded in the throw message
/// "Must rebuild at least one quarter.") is that non-positive inputs are
/// illegal and must throw — never silently no-op. A regression that
/// dropped the boundary (e.g. `quarters &lt; 0` instead of `quarters &lt;= 0`,
/// or removing the guard entirely) would let `quarters = 0` fall through
/// to `Take(0)`, which `IQueryable` evaluates to an empty sequence —
/// `RebuildReportDates` would then log "Rebuilding 0 quarter(s)" and do
/// nothing. The backoffice "rebuild recent" action would return success
/// without rebuilding anything. ArgumentOutOfRangeException is the
/// documented signal callers rely on to surface that as a visible failure.
/// </summary>
public class HoldingsAggregateRefreshServiceRebuildRecentAsyncZeroQuartersTests
{
    [Fact]
    public async Task RebuildRecentAsync_ZeroQuarters_ThrowsArgumentOutOfRangeException()
    {
        var sut = new HoldingsAggregateRefreshService(
            Substitute.For<IServiceScopeFactory>(),
            NullLogger<HoldingsAggregateRefreshService>.Instance
        );

        var act = async () => await sut.RebuildRecentAsync(0, CancellationToken.None);

        var assertion = await act.Should()
            .ThrowAsync<ArgumentOutOfRangeException>(
                "non-positive quarter counts must surface as a visible failure, not a silent no-op"
            );
        assertion.Which.ParamName.Should().Be("quarters");
    }
}
