using Equibles.CorporateActions.BusinessLogic;
using Equibles.CorporateActions.Data.Models;
using Equibles.Integrations.Yahoo.Models;
using Equibles.Yahoo.HostedService.Services;

namespace Equibles.UnitTests.Yahoo;

// The CXAI, WLFC, STKH, HBIA and NVDA fixtures are recorded production series; the rest are
// synthetic edges of the same rule.
public class YahooPriceImportServiceSplitCertificationWindowTests
{
    [Fact]
    public void CertifiableSplits_MixedBasisIslandsBeforeTheEffectiveDate_KeepsTheSplitPending()
    {
        // CXAI 1:50 effective 2026-08-18: as-traded bars sit between restated ones.
        var split = Split(new DateOnly(2026, 8, 18), 1m, 50m);
        List<HistoricalPrice> serve =
        [
            Bar(new DateOnly(2026, 8, 3), 0.14m, 6_574_700),
            Bar(new DateOnly(2026, 8, 4), 0.143m, 3_402_200),
            Bar(new DateOnly(2026, 8, 5), 0.142m, 2_726_800),
            Bar(new DateOnly(2026, 8, 6), 7.00m, 53_224),
            Bar(new DateOnly(2026, 8, 7), 0.16m, 8_524_400),
            Bar(new DateOnly(2026, 8, 10), 0.163m, 6_640_400),
            Bar(new DateOnly(2026, 8, 11), 8.15m, 70_882),
            Bar(new DateOnly(2026, 8, 14), 5.55m, 868_030),
            Bar(new DateOnly(2026, 8, 17), 4.45m, 349_550),
            Bar(new DateOnly(2026, 8, 18), 4.26m, 2_369_000),
            Bar(new DateOnly(2026, 8, 19), 4.59m, 667_200),
        ];

        YahooPriceImportService.CertifiableSplits([split], serve).Should().BeEmpty();
    }

    [Fact]
    public void CertifiableSplits_JumpTheSessionBeforeTheCapturedDate_KeepsTheSplitPending()
    {
        // WLFC 3:1 captured 2026-07-21; the series jumps on 07-20 and has no 07-21 bar.
        var split = Split(new DateOnly(2026, 7, 21), 3m, 1m);
        List<HistoricalPrice> serve =
        [
            Bar(new DateOnly(2026, 7, 16), 196.00m, 160_800),
            Bar(new DateOnly(2026, 7, 17), 191.65m, 126_900),
            Bar(new DateOnly(2026, 7, 20), 63.12m, 253_800),
            Bar(new DateOnly(2026, 7, 22), 66.32m, 206_600),
            Bar(new DateOnly(2026, 7, 23), 67.49m, 210_000),
        ];

        YahooPriceImportService.CertifiableSplits([split], serve).Should().BeEmpty();
    }

    [Fact]
    public void CertifiableSplits_JumpTheSessionAfterTheCapturedDate_KeepsTheSplitPending()
    {
        // STKH 1:3 captured 2026-07-27; the unrestated jump lands on 07-28.
        var split = Split(new DateOnly(2026, 7, 27), 1m, 3m);
        List<HistoricalPrice> serve =
        [
            Bar(new DateOnly(2026, 7, 23), 1.383m, 417_400),
            Bar(new DateOnly(2026, 7, 24), 1.269m, 70_600),
            Bar(new DateOnly(2026, 7, 27), 1.35m, 186_200),
            Bar(new DateOnly(2026, 7, 28), 3.60m, 33_590_300),
            Bar(new DateOnly(2026, 7, 29), 2.71m, 6_323_600),
        ];

        YahooPriceImportService.CertifiableSplits([split], serve).Should().BeEmpty();
    }

    [Fact]
    public void HasSplitBasisJumpNearEffectiveDate_OnlyTheInverseRatioAppears_StillFlagsTheBreak()
    {
        // Deep bars double-adjusted by a restatement of an already-adjusted serve: the boundary is
        // continuous and the only remaining jump runs the other way (25 -> 50 for a 2:1 split).
        var effective = new DateOnly(2026, 6, 15);
        var bars = new[]
        {
            Traded(new DateOnly(2026, 6, 8), 25.00m),
            Traded(new DateOnly(2026, 6, 9), 25.40m),
            Traded(new DateOnly(2026, 6, 10), 50.60m),
            Traded(new DateOnly(2026, 6, 12), 50.10m),
            Traded(new DateOnly(2026, 6, 15), 50.30m),
        };

        YahooPriceImportService
            .HasSplitBasisJumpNearEffectiveDate(bars, effective, 2m, 1m)
            .Should()
            .BeTrue();
    }

    [Fact]
    public void HasSplitBasisJumpNearEffectiveDate_StaleZeroVolumeQuote_IsIgnored()
    {
        var effective = new DateOnly(2024, 6, 10);
        var bars = new[]
        {
            Traded(new DateOnly(2024, 6, 6), 120.998m),
            (new DateOnly(2024, 6, 7), 1_208.88m, 0L),
            Traded(new DateOnly(2024, 6, 10), 121.79m),
            Traded(new DateOnly(2024, 6, 11), 120.91m),
        };

        YahooPriceImportService
            .HasSplitBasisJumpNearEffectiveDate(bars, effective, 10m, 1m)
            .Should()
            .BeFalse();
    }

    [Fact]
    public void HasSplitBasisJumpNearEffectiveDate_TradedPairAcrossStaleQuotes_FlagsTheBreak()
    {
        // HBIA 2:1 effective 2026-06-09: the zero-volume 49.505 quote is noise, but the traded
        // 99.01 -> 49.49 pair on 06-17 is an unrestated boundary.
        var effective = new DateOnly(2026, 6, 9);
        var bars = new[]
        {
            Traded(new DateOnly(2026, 6, 4), 99.01m),
            (new DateOnly(2026, 6, 8), 49.505m, 0L),
            (new DateOnly(2026, 6, 9), 99.01m, 0L),
            Traded(new DateOnly(2026, 6, 17), 49.49m),
        };

        YahooPriceImportService
            .HasSplitBasisJumpNearEffectiveDate(bars, effective, 2m, 1m)
            .Should()
            .BeTrue();
    }

    [Fact]
    public void CertifiableSplits_AdjustedServeWithAnOrdinaryMove_AllowsStamping()
    {
        // NVDA 10:1 effective 2024-06-10, as served adjusted, plus a 20% move that no 10:1 basis
        // break could produce.
        var split = Split(new DateOnly(2024, 6, 10), 10m, 1m);
        List<HistoricalPrice> serve =
        [
            Bar(new DateOnly(2024, 6, 6), 120.998m, 664_696_000),
            Bar(new DateOnly(2024, 6, 7), 120.888m, 412_386_000),
            Bar(new DateOnly(2024, 6, 10), 121.79m, 313_434_100),
            Bar(new DateOnly(2024, 6, 11), 120.91m, 222_551_200),
            Bar(new DateOnly(2024, 6, 12), 96.70m, 300_000_000),
        ];

        YahooPriceImportService.CertifiableSplits([split], serve).Should().ContainSingle();
    }

    [Fact]
    public void CertifiableSplits_JumpOutsideTheSameEventWindow_DoesNotHoldTheSplit()
    {
        var split = Split(new DateOnly(2026, 3, 16), 1m, 4m);
        List<HistoricalPrice> serve =
        [
            Bar(new DateOnly(2026, 2, 20), 1.00m, 50_000),
            Bar(new DateOnly(2026, 2, 23), 4.10m, 50_000),
            Bar(new DateOnly(2026, 3, 13), 4.00m, 50_000),
            Bar(new DateOnly(2026, 3, 16), 4.05m, 50_000),
        ];

        YahooPriceImportService.CertifiableSplits([split], serve).Should().ContainSingle();
    }

    [Fact]
    public void CertifiableSplits_TwoSplitsInOneWindowWithOneUnrestated_HoldsBoth()
    {
        // WHLR's 1:6 and 1:5 eight days apart sit inside each other's window and within the ratio
        // tolerance, so one unrestated boundary keeps both pending; that is the conservative side.
        var first = Split(new DateOnly(2024, 6, 20), 1m, 6m);
        var second = Split(new DateOnly(2024, 6, 28), 1m, 5m);
        List<HistoricalPrice> serve =
        [
            Bar(new DateOnly(2024, 6, 18), 1.00m, 90_000),
            Bar(new DateOnly(2024, 6, 19), 1.05m, 80_000),
            Bar(new DateOnly(2024, 6, 20), 6.10m, 20_000),
            Bar(new DateOnly(2024, 6, 27), 6.20m, 15_000),
            Bar(new DateOnly(2024, 6, 28), 6.10m, 16_000),
            Bar(new DateOnly(2024, 7, 1), 6.00m, 14_000),
        ];

        YahooPriceImportService.CertifiableSplits([first, second], serve).Should().BeEmpty();
    }

    [Theory]
    [InlineData(-10, true)]
    [InlineData(-11, false)]
    [InlineData(10, true)]
    [InlineData(11, false)]
    public void HasSplitBasisJumpNearEffectiveDate_CountsJumpsOnlyInsideTheInclusiveWindow(
        int jumpOffsetDays,
        bool flagged
    )
    {
        var effective = new DateOnly(2026, 5, 20);
        var jumpDate = effective.AddDays(jumpOffsetDays);
        var bars = new[]
        {
            Traded(jumpDate.AddDays(-1), 1.00m),
            Traded(jumpDate, 4.00m),
            Traded(effective.AddDays(12), 4.02m),
        };

        YahooPriceImportService
            .HasSplitBasisJumpNearEffectiveDate(bars, effective, 1m, 4m)
            .Should()
            .Be(flagged);
    }

    [Fact]
    public void CertifiableSplits_GenuineRatioSizedMoveNearTheSplit_KeepsTheSplitPending()
    {
        // Accepted trade-off: a real +60% day beside a 1:2 reverse split is indistinguishable from an
        // unrestated boundary, so the split stays unconfirmed rather than risk a false certification.
        var split = Split(new DateOnly(2026, 4, 13), 1m, 2m);
        List<HistoricalPrice> serve =
        [
            Bar(new DateOnly(2026, 4, 9), 2.00m, 400_000),
            Bar(new DateOnly(2026, 4, 10), 2.04m, 380_000),
            Bar(new DateOnly(2026, 4, 13), 2.02m, 900_000),
            Bar(new DateOnly(2026, 4, 14), 3.25m, 9_000_000),
        ];

        YahooPriceImportService.CertifiableSplits([split], serve).Should().BeEmpty();
    }

    [Fact]
    public void CertifiableSplits_InvalidCandleTheReplacementDrops_IsNotCertified()
    {
        // The replacement never stores an impossible candle, so it must not certify against one.
        var split = Split(new DateOnly(2024, 6, 10), 10m, 1m);
        List<HistoricalPrice> serve =
        [
            Bar(new DateOnly(2024, 6, 6), 120.998m, 664_696_000),
            Bar(new DateOnly(2024, 6, 7), 120.888m, 412_386_000),
            new()
            {
                Date = new DateOnly(2024, 6, 10),
                Open = 1_217.90m,
                High = 1_100.00m,
                Low = 1_170.10m,
                Close = 1_217.90m,
                AdjustedClose = 1_217.90m,
                Volume = 31_343_410,
            },
            Bar(new DateOnly(2024, 6, 11), 120.91m, 222_551_200),
        ];

        YahooPriceImportService.CertifiableSplits([split], serve).Should().ContainSingle();
    }

    private static PendingSplitSnapshot Split(
        DateOnly effectiveDate,
        decimal numerator,
        decimal denominator
    ) => new(Guid.NewGuid(), effectiveDate, numerator, denominator, StockSplitSource.Yahoo);

    private static (DateOnly Date, decimal Close, long Volume) Traded(
        DateOnly date,
        decimal close
    ) => (date, close, 10_000);

    private static HistoricalPrice Bar(DateOnly date, decimal close, long volume) =>
        new()
        {
            Date = date,
            Open = close,
            High = close,
            Low = close,
            Close = close,
            AdjustedClose = close,
            Volume = volume,
        };
}
