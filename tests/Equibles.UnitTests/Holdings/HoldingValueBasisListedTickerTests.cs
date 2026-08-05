using Equibles.CorporateActions.Data.Models;
using Equibles.Holdings.HostedService.Services;

namespace Equibles.UnitTests.Holdings;

/// <summary>
/// The per-listing split-attribution rules (#4247). A sibling-class 13F position is
/// valued from its OWN class's price series, so its share count may only be restated
/// by splits attributed to that same series. The classes of one issuer do not have to
/// split together (Alphabet's A and C did; plenty of preferred/unit siblings do not),
/// and the stored split table attributes each capture to exactly one price series via
/// <see cref="StockSplit.PriceSeriesTicker"/> — null meaning the pre-attribution
/// legacy capture, which only the primary series could have produced.
/// </summary>
public class HoldingValueBasisListedTickerTests
{
    private static StockSplit Applied(
        DateOnly effectiveDate,
        decimal numerator,
        decimal denominator,
        string priceSeriesTicker
    ) =>
        new()
        {
            EffectiveDate = effectiveDate,
            Numerator = numerator,
            Denominator = denominator,
            PriceSeriesTicker = priceSeriesTicker,
            PriceAdjustmentAppliedTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        };

    [Fact]
    public void SecondaryPosition_SplitAttributedToItsOwnListing_RestatesTheCount()
    {
        // The happy path per-class split capture unlocks: a GOOG split recorded against the
        // GOOG series restates GOOG positions exactly like a primary split restates primary ones.
        var splits = new List<StockSplit> { Applied(new DateOnly(2022, 7, 18), 20m, 1m, "GOOG") };

        HoldingValueBasis
            .TryResolveShareCountFactor(
                new DateOnly(2022, 3, 31),
                splits,
                "GOOG",
                "GOOGL",
                ["GOOG"],
                out var factor
            )
            .Should()
            .BeTrue();

        factor.Should().Be(20m);
    }

    [Fact]
    public void SecondaryPosition_UnattributedPostReportSplit_RefusesToResolve()
    {
        // A legacy null-attribution split proves the ISSUER split after the report date, but says
        // nothing about whether the secondary class's own series moved. Guessing "the classes
        // split together" would be exactly the BRK-A-at-BRK-B's-price class of error — the row
        // must stay pending until per-class capture can attribute it.
        var splits = new List<StockSplit> { Applied(new DateOnly(2022, 7, 18), 20m, 1m, null) };

        HoldingValueBasis
            .TryResolveShareCountFactor(
                new DateOnly(2022, 3, 31),
                splits,
                "GOOG",
                "GOOGL",
                ["GOOG"],
                out var factor
            )
            .Should()
            .BeFalse();

        factor.Should().Be(1m, "an unusable factor must be inert, never silently applied");
    }

    [Fact]
    public void SecondaryPosition_SplitAttributedToAnotherSeries_RefusesToResolve()
    {
        // Same rule when the post-report split is attributed to the PRIMARY series: the issuer
        // demonstrably restructured after the report date and the secondary's basis is unknowable.
        var splits = new List<StockSplit> { Applied(new DateOnly(2022, 7, 18), 20m, 1m, "GOOGL") };

        HoldingValueBasis
            .TryResolveShareCountFactor(
                new DateOnly(2022, 3, 31),
                splits,
                "GOOG",
                "GOOGL",
                ["GOOG"],
                out var factor
            )
            .Should()
            .BeFalse();
    }

    [Fact]
    public void SecondaryPosition_ForeignSeriesSplitBeforeReportDate_IsIgnored()
    {
        // Only POST-report splits can move a count; an earlier split of any series is history the
        // filed count already absorbed. The secondary must not be parked pending over it.
        var splits = new List<StockSplit> { Applied(new DateOnly(2020, 8, 31), 4m, 1m, "GOOGL") };

        HoldingValueBasis
            .TryResolveShareCountFactor(
                new DateOnly(2022, 3, 31),
                splits,
                "GOOG",
                "GOOGL",
                ["GOOG"],
                out var factor
            )
            .Should()
            .BeTrue();

        factor.Should().Be(1m);
    }

    [Fact]
    public void PrimaryPosition_SplitAttributedToItsOwnSymbol_RestatesTheCount()
    {
        // A primary row must treat a split attributed to the primary's own symbol exactly like a
        // legacy unattributed one — the attribution column filling in must not turn restatement off.
        var splits = new List<StockSplit> { Applied(new DateOnly(2024, 6, 10), 10m, 1m, "NVDA") };

        HoldingValueBasis
            .TryResolveShareCountFactor(
                new DateOnly(2023, 12, 31),
                splits,
                null,
                "NVDA",
                null,
                out var factor
            )
            .Should()
            .BeTrue();

        factor.Should().Be(10m);
    }

    [Fact]
    public void PrimaryPosition_SplitAttributedToAStaleSymbol_RefusesToResolve()
    {
        // The capture manager preserves a split's attribution verbatim forever, so after a
        // primary RENAME (LC → HAPN) the stored attribution matches neither the current
        // primary nor any secondary. That split very likely IS the primary series' own —
        // silently skipping it publishes a value off by exactly the ratio, the error class
        // this file exists to prevent. Unknown basis: the row stays pending instead.
        var splits = new List<StockSplit> { Applied(new DateOnly(2024, 6, 10), 10m, 1m, "LC") };

        HoldingValueBasis
            .TryResolveShareCountFactor(
                new DateOnly(2023, 12, 31),
                splits,
                null,
                "HAPN",
                [],
                out var factor
            )
            .Should()
            .BeFalse();

        factor.Should().Be(1m, "an unusable factor must be inert, never silently applied");
    }

    [Fact]
    public void PrimaryPosition_SplitAttributedToASecondaryListing_MovesNothing()
    {
        // A sibling class splitting does not restate the primary's count; the primary resolves
        // with factor 1 rather than picking up a foreign series' ratio or parking pending.
        var splits = new List<StockSplit> { Applied(new DateOnly(2024, 6, 10), 10m, 1m, "BRK-A") };

        HoldingValueBasis
            .TryResolveShareCountFactor(
                new DateOnly(2023, 12, 31),
                splits,
                null,
                "BRK-B",
                ["BRK-A"],
                out var factor
            )
            .Should()
            .BeTrue();

        factor.Should().Be(1m);
    }
}
