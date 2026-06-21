using Equibles.Congress.Data.Models;
using Equibles.Congress.HostedService.Models;
using Equibles.Congress.HostedService.Services;

namespace Equibles.UnitTests.Congress;

public class CongressionalAnnualDisclosureSyncServiceNegativeNetWorthTests
{
    private static AnnualDisclosureLineItem Liability(long minimum, long maximum) =>
        new()
        {
            Kind = CongressionalDisclosureLineKind.Liability,
            Description = "Liability",
            RangeMinimum = minimum,
            RangeMaximum = maximum,
        };

    // Contract: minimum = Σ asset minimums − Σ liability maximums; maximum =
    // Σ asset maximums − Σ liability minimums. A member who discloses only
    // liabilities (net debt) must therefore yield a fully negative band — the
    // methodology brackets the truth and is never clamped at zero.
    [Fact]
    public void ComputeNetWorthBand_LiabilitiesOnlyNoAssets_ProducesNegativeBand()
    {
        var (minimum, maximum) = CongressionalAnnualDisclosureSyncService.ComputeNetWorthBand([
            Liability(100_000, 250_000),
            Liability(15_001, 50_000),
        ]);

        // No assets → 0 − Σ liability maxes / mins.
        minimum.Should().Be(-(250_000 + 50_000));
        maximum.Should().Be(-(100_000 + 15_001));
        minimum.Should().BeLessThan(maximum);
    }
}
