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
}
