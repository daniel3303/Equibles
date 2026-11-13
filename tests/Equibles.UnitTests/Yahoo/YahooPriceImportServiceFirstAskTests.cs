using Equibles.Yahoo.HostedService.Services;

namespace Equibles.UnitTests.Yahoo;

public class YahooPriceImportServiceFirstAskTests
{
    private static readonly DateOnly Settled = new(2026, 11, 13);

    [Fact]
    public void AUsSeriesIsAFirstAskOncePerSettledDay()
    {
        var target = new PriceSeriesTarget("ABC", Guid.NewGuid(), Guid.NewGuid(), true);

        YahooPriceImportService.IsFirstAsk(target, Settled, null).Should().BeTrue();
        YahooPriceImportService.IsFirstAsk(target, Settled, null).Should().BeFalse();
        YahooPriceImportService.IsFirstAsk(target, Settled.AddDays(1), null).Should().BeTrue();
    }

    [Fact]
    public void HistoricalRecoveryIsNeverAFirstAsk()
    {
        var target = new PriceSeriesTarget("OLD", Guid.NewGuid(), Guid.NewGuid(), true)
        {
            IsHistorical = true,
        };

        YahooPriceImportService.IsFirstAsk(target, Settled, null).Should().BeFalse();
    }

    [Fact]
    public void ACatalogSeriesFollowsTheQueuesFirstTier()
    {
        var target = new PriceSeriesTarget(
            "SAP",
            Guid.NewGuid(),
            Guid.NewGuid(),
            true,
            MarketCountryCode: "DE",
            MarketIdentifierCode: "XETR"
        );

        YahooPriceImportService.IsFirstAsk(target, Settled, null).Should().BeFalse();
        YahooPriceImportService
            .IsFirstAsk(target, Settled, new HashSet<Guid> { target.EquityListingId })
            .Should()
            .BeTrue();
    }
}
