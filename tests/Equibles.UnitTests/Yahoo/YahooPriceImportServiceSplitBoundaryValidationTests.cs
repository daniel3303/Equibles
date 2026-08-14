using Equibles.Integrations.Yahoo.Models;
using Equibles.Yahoo.HostedService.Services;

namespace Equibles.UnitTests.Yahoo;

public class YahooPriceImportServiceSplitBoundaryValidationTests
{
    [Theory]
    [InlineData(0.30, 5.71, 1, 16)]
    [InlineData(100, 24, 4, 1)]
    public void IsSplitBoundaryDiscontinuous_JumpMatchesSplitRatio_ReturnsTrue(
        decimal closeBefore,
        decimal closeAfter,
        decimal numerator,
        decimal denominator
    )
    {
        YahooPriceImportService
            .IsSplitBoundaryDiscontinuous(closeBefore, closeAfter, numerator, denominator)
            .Should()
            .BeTrue();
    }

    [Fact]
    public void IsSplitBoundaryDiscontinuous_ContinuousSeries_ReturnsFalse()
    {
        YahooPriceImportService
            .IsSplitBoundaryDiscontinuous(5.45m, 5.71m, 1m, 16m)
            .Should()
            .BeFalse();
    }

    [Fact]
    public void HasSplitBasisDiscontinuity_UsesNearestSessionsAroundEffectiveDate()
    {
        HistoricalPrice[] prices =
        [
            new() { Date = new DateOnly(2026, 8, 6), Close = 0.28m },
            new() { Date = new DateOnly(2026, 8, 7), Close = 0.30m },
            new() { Date = new DateOnly(2026, 8, 10), Close = 5.71m },
            new() { Date = new DateOnly(2026, 8, 11), Close = 5.60m },
        ];

        YahooPriceImportService
            .HasSplitBasisDiscontinuity(prices, new DateOnly(2026, 8, 10), 1m, 16m)
            .Should()
            .BeTrue();
    }

    [Fact]
    public void IsSplitBoundaryDiscontinuous_SmallRatioChange_IsNotClassified()
    {
        YahooPriceImportService
            .IsSplitBoundaryDiscontinuous(1.50m, 1m, 3m, 2m)
            .Should()
            .BeFalse();
    }
}
