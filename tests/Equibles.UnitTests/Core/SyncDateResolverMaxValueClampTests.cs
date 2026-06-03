using Equibles.Core.Configuration;
using Equibles.Worker;

namespace Equibles.UnitTests.Core;

public class SyncDateResolverMaxValueClampTests
{
    [Fact]
    public void Resolve_LatestDateIsMaxValue_ReturnsMaxValueNotAdvanced()
    {
        // Contract: a non-default latest date advances by one day, EXCEPT DateOnly.MaxValue
        // which saturates to itself (AddDays(1) would overflow). The sibling overflow test only
        // asserts DoesNotThrow — a regression that returned default/fallback would still pass it.
        // Pin the clamp VALUE: MaxValue in must yield MaxValue out.
        var result = SyncDateResolver.Resolve(DateOnly.MaxValue, new WorkerOptions());

        result.Should().Be(DateOnly.MaxValue);
    }
}
