using Equibles.CorporateActions.BusinessLogic;
using Equibles.CorporateActions.Data.Models;
using Equibles.Integrations.Yahoo.Models;
using Equibles.Yahoo.HostedService.Services;

namespace Equibles.UnitTests.Yahoo;

public class YahooPriceImportServiceSplitRestatementTests
{
    private static readonly DateOnly Today = new(2026, 8, 18);

    private static List<HistoricalPrice> StraddlingReverseSplitServe() =>
        [
            new()
            {
                Date = new DateOnly(2026, 8, 11),
                Open = 0.4388m,
                High = 0.45m,
                Low = 0.41m,
                Close = 0.4176m,
                AdjustedClose = 0.418m,
                Volume = 143_909_501,
            },
            new()
            {
                Date = new DateOnly(2026, 8, 14),
                Open = 12.3m,
                High = 13.5m,
                Low = 12.1m,
                Close = 13.47m,
                AdjustedClose = 13.47m,
                Volume = 4_100_000,
            },
            new()
            {
                Date = new DateOnly(2026, 8, 17),
                Open = 13.4m,
                High = 13.6m,
                Low = 11.5m,
                Close = 11.63m,
                AdjustedClose = 11.63m,
                Volume = 3_200_000,
            },
        ];

    [Fact]
    public void RestateHistoryAcrossKnownSplits_StraddlingReverseSplit_RestatesPreEffectiveRows()
    {
        var prices = StraddlingReverseSplitServe();
        var splits = new[] { new SplitBasisDefinition(new DateOnly(2026, 8, 14), 1m, 30m) };

        var restated = YahooPriceImportService.RestateHistoryAcrossKnownSplits(
            prices,
            splits,
            Today
        );

        restated.Should().Be(1);
        prices[0].Close.Should().Be(12.528m);
        prices[0].Open.Should().Be(13.164m);
        prices[0].AdjustedClose.Should().Be(12.54m);
        prices[0].Volume.Should().Be(4_796_983); // 143,909,501 / 30, rounded
        // Post-effective rows keep the served values untouched.
        prices[1].Close.Should().Be(13.47m);
        prices[2].Volume.Should().Be(3_200_000);

        YahooPriceImportService
            .HasSplitBasisDiscontinuity(prices, new DateOnly(2026, 8, 14), 1m, 30m)
            .Should()
            .BeFalse();
    }

    [Fact]
    public void RestateHistoryAcrossKnownSplits_AlreadyAdjustedServe_TouchesNothing()
    {
        var prices = StraddlingReverseSplitServe();
        // The provider already restated: pre-effective rows sit on the post-split basis.
        prices[0].Close = 12.53m;
        prices[0].Open = 13.16m;
        var splits = new[] { new SplitBasisDefinition(new DateOnly(2026, 8, 14), 1m, 30m) };

        var restated = YahooPriceImportService.RestateHistoryAcrossKnownSplits(
            prices,
            splits,
            Today
        );

        restated.Should().Be(0);
        prices[0].Close.Should().Be(12.53m);
        prices[0].Volume.Should().Be(143_909_501);
    }

    [Fact]
    public void RestateHistoryAcrossKnownSplits_FutureSplit_TouchesNothing()
    {
        var prices = StraddlingReverseSplitServe();
        var splits = new[] { new SplitBasisDefinition(new DateOnly(2026, 8, 14), 1m, 30m) };

        var restated = YahooPriceImportService.RestateHistoryAcrossKnownSplits(
            prices,
            splits,
            today: new DateOnly(2026, 8, 13)
        );

        restated.Should().Be(0);
        prices[0].Close.Should().Be(0.4176m);
    }

    [Fact]
    public void RestateHistoryAcrossKnownSplits_JumpNotMatchingRatio_TouchesNothing()
    {
        var prices = StraddlingReverseSplitServe();
        // A 2:1 captured ratio cannot explain a ~30x boundary jump; restating would corrupt.
        var splits = new[] { new SplitBasisDefinition(new DateOnly(2026, 8, 14), 1m, 2m) };

        var restated = YahooPriceImportService.RestateHistoryAcrossKnownSplits(
            prices,
            splits,
            Today
        );

        restated.Should().Be(0);
        prices[0].Close.Should().Be(0.4176m);
    }

    [Fact]
    public void RestateHistoryAcrossKnownSplits_ForwardSplit_ScalesInTheOppositeDirection()
    {
        List<HistoricalPrice> prices =
        [
            new()
            {
                Date = new DateOnly(2026, 6, 1),
                Open = 100m,
                High = 104m,
                Low = 98m,
                Close = 102m,
                AdjustedClose = 101m,
                Volume = 1_000_000,
            },
            new()
            {
                Date = new DateOnly(2026, 6, 2),
                Open = 25.2m,
                High = 26m,
                Low = 25m,
                Close = 25.6m,
                AdjustedClose = 25.6m,
                Volume = 4_100_000,
            },
        ];
        var splits = new[] { new SplitBasisDefinition(new DateOnly(2026, 6, 2), 4m, 1m) };

        var restated = YahooPriceImportService.RestateHistoryAcrossKnownSplits(
            prices,
            splits,
            Today
        );

        restated.Should().Be(1);
        prices[0].Close.Should().Be(25.5m);
        prices[0].Volume.Should().Be(4_000_000);
        prices[1].Close.Should().Be(25.6m);
    }

    [Fact]
    public void RestateHistoryAcrossKnownSplits_DuplicateBoundaryDefinitions_RestatesOnce()
    {
        var prices = StraddlingReverseSplitServe();
        // The reconcile path merges captured DB splits with chart-reported events; the same
        // boundary can arrive twice. The second pass finds no remaining jump and must skip.
        var splits = new[]
        {
            new SplitBasisDefinition(new DateOnly(2026, 8, 14), 1m, 30m),
            new SplitBasisDefinition(new DateOnly(2026, 8, 14), 1m, 30m),
        };

        var restated = YahooPriceImportService.RestateHistoryAcrossKnownSplits(
            prices,
            splits,
            Today
        );

        restated.Should().Be(1);
        prices[0].Close.Should().Be(12.528m);
    }

    [Fact]
    public void CertifiableSplits_ServeEndingBeforeEffectiveDate_KeepsTheSplitPending()
    {
        var effective = new PendingSplitSnapshot(
            Guid.NewGuid(),
            new DateOnly(2026, 8, 10),
            1m,
            30m,
            StockSplitSource.External
        );
        var notServedYet = new PendingSplitSnapshot(
            Guid.NewGuid(),
            new DateOnly(2026, 8, 14),
            1m,
            30m,
            StockSplitSource.External
        );

        var certifiable = YahooPriceImportService.CertifiableSplits(
            [effective, notServedYet],
            lastServedDate: new DateOnly(2026, 8, 13)
        );

        certifiable.Should().ContainSingle().Which.Should().Be(effective);
    }

    [Fact]
    public void CertifiableSplits_ServeReachingTheEffectiveDate_AllowsStamping()
    {
        var split = new PendingSplitSnapshot(
            Guid.NewGuid(),
            new DateOnly(2026, 8, 14),
            1m,
            30m,
            StockSplitSource.External
        );

        var certifiable = YahooPriceImportService.CertifiableSplits(
            [split],
            lastServedDate: new DateOnly(2026, 8, 14)
        );

        certifiable.Should().ContainSingle();
    }
}
