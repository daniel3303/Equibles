using Equibles.Holdings.HostedService.Services;

namespace Equibles.Tests.Holdings;

public class HoldingsParsingHelperTests {
    [Fact]
    public void TryParseDateOnly_SecFormat_ReturnsExpectedDate() {
        var success = HoldingsParsingHelper.TryParseDateOnly("15-MAR-2024", out var result);

        success.Should().BeTrue();
        result.Should().Be(new DateOnly(2024, 3, 15));
    }
}
