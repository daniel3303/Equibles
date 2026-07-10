using Equibles.Fred.Data.Models;
using Equibles.Fred.HostedService.Services;

namespace Equibles.UnitTests.Fred;

public class CuratedReleaseImportanceRegistryTests
{
    [Theory]
    [InlineData(9, FredReleaseImportance.High)] // Retail sales
    [InlineData(10, FredReleaseImportance.High)] // CPI
    [InlineData(46, FredReleaseImportance.High)] // PPI
    [InlineData(50, FredReleaseImportance.High)] // Employment Situation
    [InlineData(53, FredReleaseImportance.High)] // GDP
    [InlineData(54, FredReleaseImportance.High)] // Personal Income and Outlays (PCE)
    [InlineData(101, FredReleaseImportance.High)] // FOMC Press Release
    [InlineData(180, FredReleaseImportance.Medium)] // Weekly claims
    [InlineData(192, FredReleaseImportance.Medium)] // JOLTS
    public void Resolve_CuratedRelease_ReturnsItsTier(int releaseId, FredReleaseImportance expected)
    {
        CuratedReleaseImportanceRegistry.Resolve(releaseId).Should().Be(expected);
    }

    [Theory]
    [InlineData(445)] // SOFR — a daily rate print, deliberately unmapped
    [InlineData(18)] // H.15 Selected Interest Rates
    [InlineData(123456)] // Never-seen release
    public void Resolve_UnmappedRelease_DefaultsToLow(int releaseId)
    {
        CuratedReleaseImportanceRegistry.Resolve(releaseId).Should().Be(FredReleaseImportance.Low);
    }
}
