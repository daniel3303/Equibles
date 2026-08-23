using Equibles.Finra.Data.Models;
using Xunit;

namespace Equibles.UnitTests.Finra;

public class OffExchangeVolumeCoverageTests
{
    [Fact]
    public void MayIncludeCaseFoldedSiblingVolume_WeekBeforeBoundary_ReturnsTrue()
    {
        var week = OffExchangeVolumeCoverage.CorrectedSymbolResolutionStartWeek.AddDays(-7);

        OffExchangeVolumeCoverage.MayIncludeCaseFoldedSiblingVolume(week).Should().BeTrue();
    }

    [Fact]
    public void MayIncludeCaseFoldedSiblingVolume_BoundaryWeek_ReturnsFalse()
    {
        OffExchangeVolumeCoverage
            .MayIncludeCaseFoldedSiblingVolume(
                OffExchangeVolumeCoverage.CorrectedSymbolResolutionStartWeek
            )
            .Should()
            .BeFalse();
    }
}
