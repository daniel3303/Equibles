using Equibles.Holdings.HostedService.Services;

namespace Equibles.UnitTests.Holdings.Intelligence;

public class HolderIntelligenceTurnoverChangeTests
{
    [Theory]
    [InlineData(null, null, false)]
    [InlineData(null, "0.1", true)]
    [InlineData("0.1", null, true)]
    [InlineData("0.123457", "0.1234565", false)]
    [InlineData("0.123456", "0.1234565", true)]
    [InlineData("0.123456", "0.1234564", false)]
    [InlineData("0.123456", "0.12345651", true)]
    public void TurnoverChanged_ComparesTheValueTheColumnWouldStore(
        string stored,
        string current,
        bool changed
    )
    {
        HolderIntelligenceRefreshService
            .TurnoverChanged(Parse(stored), Parse(current))
            .Should()
            .Be(changed);
    }

    private static decimal? Parse(string value) =>
        value == null
            ? null
            : decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
}
