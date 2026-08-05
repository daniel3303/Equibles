using Equibles.CorporateActions.Data.Models;
using Equibles.Holdings.HostedService.Services;

namespace Equibles.UnitTests.Holdings;

public class HoldingValueBasisBoundaryAndCompoundTests
{
    private static StockSplit Applied(
        DateOnly effectiveDate,
        decimal numerator,
        decimal denominator
    ) =>
        new()
        {
            EffectiveDate = effectiveDate,
            Numerator = numerator,
            Denominator = denominator,
            PriceAdjustmentAppliedTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        };

    [Fact]
    public void TryResolveShareCountFactor_SplitEffectiveOnTheReportDate_DoesNotRestate()
    {
        // A quarter-end report dated on the effective date already counts post-split shares, so
        // restating it would apply the ratio to a count that has already absorbed it. The strict
        // comparison is the same boundary SplitAdjustment.ShareCountFactor uses; the two must not
        // drift apart, or share counts and values would disagree on exactly one day per split.
        var splits = new List<StockSplit> { Applied(new DateOnly(2024, 6, 30), 10m, 1m) };

        HoldingValueBasis
            .TryResolveShareCountFactor(new DateOnly(2024, 6, 30), splits, null, "TEST", out var factor)
            .Should()
            .BeTrue();

        factor.Should().Be(1m);
    }

    [Fact]
    public void TryResolveShareCountFactor_SeveralSplitsAfterReportDate_CompoundsThem()
    {
        // NVDA really did split twice inside the window we hold prices for — 4:1 in 2021 and 10:1
        // in 2024 — so a 2020 position has to be restated by both. Applying only the most recent
        // one leaves a 4x error that looks nothing like a split and would be very hard to trace.
        var splits = new List<StockSplit>
        {
            Applied(new DateOnly(2021, 7, 20), 4m, 1m),
            Applied(new DateOnly(2024, 6, 10), 10m, 1m),
        };

        HoldingValueBasis
            .TryResolveShareCountFactor(new DateOnly(2020, 12, 31), splits, null, "TEST", out var factor)
            .Should()
            .BeTrue();

        factor.Should().Be(40m);
    }

    [Fact]
    public void TryResolveShareCountFactor_NoSplits_LeavesTheCountAlone()
    {
        // The overwhelming majority of positions are on stocks that never split, so the quiet
        // path has to be an exact no-op: a factor of anything but 1 here would perturb every
        // value on the platform.
        HoldingValueBasis
            .TryResolveShareCountFactor(new DateOnly(2024, 6, 30), [], null, "TEST", out var factor)
            .Should()
            .BeTrue();

        factor.Should().Be(1m);
    }

    [Fact]
    public void TryResolveShareCountFactor_MalformedRatio_SkipsItRatherThanDividingByZero()
    {
        // A zero denominator is a broken capture, not an ambiguous basis: it moves no count, so it
        // must neither divide by zero nor park the row as unresolvable. The good split beside it
        // still has to apply.
        var splits = new List<StockSplit>
        {
            Applied(new DateOnly(2025, 1, 2), 3m, 0m),
            Applied(new DateOnly(2025, 6, 2), 2m, 1m),
        };

        HoldingValueBasis
            .TryResolveShareCountFactor(new DateOnly(2024, 12, 31), splits, null, "TEST", out var factor)
            .Should()
            .BeTrue();

        factor.Should().Be(2m);
    }
}
