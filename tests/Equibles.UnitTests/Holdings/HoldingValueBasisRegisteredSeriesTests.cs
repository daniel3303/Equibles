using Equibles.CommonStocks.Data.Models;
using Equibles.CorporateActions.Data.Models;
using Equibles.Holdings.HostedService.Services;

namespace Equibles.UnitTests.Holdings;

/// <summary>
/// One trust (iShares Trust, CIK 1100663) lists hundreds of separate funds under one issuer. The
/// splits, listings and series ids below are the production rows that kept every IVV 13F position
/// pending: other funds of the trust split after the report date, and the secondary-listing rule
/// read that as "this class's basis is unknown".
/// </summary>
public class HoldingValueBasisRegisteredSeriesTests
{
    private static readonly DateOnly ReportDate = new(2025, 12, 31);

    private static readonly Guid Issuer = Guid.Parse("f12efb68-5b14-48e4-8d78-4b03ae682c00");

    private static readonly IReadOnlyDictionary<string, string> TrustSeries =
        RegisteredSeriesTickers.Build([
            (Issuer, "S000022496", ["AAXJ"]),
            (Issuer, "S000004310", ["IVV"]),
            (Issuer, "S000004305", ["IXN"]),
            (Issuer, "S000004346", ["IWF"]),
            (Issuer, "S000031844", ["HDV"]),
            (Issuer, "S000069558", ["GOVZ"]),
        ])[Issuer];

    private static StockSplit ListingSplit(
        string ticker,
        DateOnly effectiveDate,
        decimal numerator,
        decimal denominator,
        DateTime? appliedAt
    )
    {
        var listing = new EquityListing { Ticker = ticker, MarketCountryCode = "US" };
        return new StockSplit
        {
            EquityIssuerId = Issuer,
            Listing = listing,
            EquityListingId = listing.Id,
            PriceSeriesTicker = ticker,
            EffectiveDate = effectiveDate,
            Numerator = numerator,
            Denominator = denominator,
            PriceAdjustmentAppliedTime = appliedAt,
        };
    }

    private static List<StockSplit> TrustSplitsAfterReportDate() =>
        [
            ListingSplit(
                "IWF",
                new DateOnly(2026, 4, 29),
                4m,
                1m,
                new DateTime(2026, 9, 22, 15, 7, 29, DateTimeKind.Utc)
            ),
            ListingSplit(
                "HDV",
                new DateOnly(2026, 4, 29),
                5m,
                1m,
                new DateTime(2026, 9, 9, 8, 5, 27, DateTimeKind.Utc)
            ),
            ListingSplit(
                "GOVZ",
                new DateOnly(2026, 5, 5),
                1m,
                4m,
                new DateTime(2026, 9, 15, 15, 56, 28, DateTimeKind.Utc)
            ),
        ];

    [Fact]
    public void SecondaryFund_SplitsOfOtherFundsOfTheTrust_ResolveWithFactorOne()
    {
        HoldingValueBasis
            .TryResolveShareCountFactor(
                ReportDate,
                TrustSplitsAfterReportDate(),
                "IVV",
                "AAXJ",
                ["IVV", "IWF", "HDV", "GOVZ"],
                out var factor,
                TrustSeries
            )
            .Should()
            .BeTrue();

        factor.Should().Be(1m);
    }

    [Fact]
    public void SecondaryFund_WithoutSeriesMap_StillDefers()
    {
        HoldingValueBasis
            .TryResolveShareCountFactor(
                ReportDate,
                TrustSplitsAfterReportDate(),
                "IVV",
                "AAXJ",
                ["IVV", "IWF", "HDV", "GOVZ"],
                out _
            )
            .Should()
            .BeFalse();
    }

    [Fact]
    public void SecondaryFund_OwnSplitAmongOtherFundsSplits_AppliesOnlyItsOwnRatio()
    {
        var splits = TrustSplitsAfterReportDate();

        HoldingValueBasis
            .TryResolveShareCountFactor(
                ReportDate,
                splits,
                "IWF",
                "AAXJ",
                ["IVV", "IWF", "HDV", "GOVZ"],
                out var factor,
                TrustSeries
            )
            .Should()
            .BeTrue();

        factor.Should().Be(4m);
    }

    [Fact]
    public void SecondaryFund_OwnSplitAwaitingPriceAdjustment_StillDefers()
    {
        List<StockSplit> splits =
        [
            .. TrustSplitsAfterReportDate(),
            ListingSplit("IVV", new DateOnly(2026, 11, 5), 3m, 1m, null),
        ];

        HoldingValueBasis
            .TryResolveShareCountFactor(
                ReportDate,
                splits,
                "IVV",
                "AAXJ",
                ["IVV", "IWF", "HDV", "GOVZ"],
                out _,
                TrustSeries
            )
            .Should()
            .BeFalse();
    }

    [Fact]
    public void SecondaryClass_SplitOfAnotherClassOfTheSameSeries_StillDefers()
    {
        // Two share classes of one series are one portfolio; their splits are not independent.
        var series = RegisteredSeriesTickers.Build([(Issuer, "S000000001", ["AAA", "AAA-B"])])[
            Issuer
        ];
        List<StockSplit> splits =
        [
            ListingSplit(
                "AAA",
                new DateOnly(2026, 4, 29),
                2m,
                1m,
                new DateTime(2026, 5, 2, 0, 0, 0, DateTimeKind.Utc)
            ),
        ];

        HoldingValueBasis
            .TryResolveShareCountFactor(
                ReportDate,
                splits,
                "AAA-B",
                "AAA",
                ["AAA-B"],
                out _,
                series
            )
            .Should()
            .BeFalse();
    }

    [Fact]
    public void SecondaryClass_SplitOfAListingWithNoSeries_StillDefers()
    {
        // An operating company's classes (LEN and LEN-B) carry no series ids; a split captured on
        // one class only says nothing about the other.
        List<StockSplit> splits =
        [
            ListingSplit(
                "LEN",
                new DateOnly(2025, 1, 21),
                1033m,
                1000m,
                new DateTime(2025, 2, 1, 0, 0, 0, DateTimeKind.Utc)
            ),
        ];

        HoldingValueBasis
            .TryResolveShareCountFactor(
                new DateOnly(2024, 12, 31),
                splits,
                "LEN-B",
                "LEN",
                ["LEN-B"],
                out _,
                TrustSeries
            )
            .Should()
            .BeFalse();
    }

    [Fact]
    public void SecondaryFund_SplitAttributedOnlyBySymbol_StillDefers()
    {
        // A split with no exact listing names its series only by a symbol that may be stale.
        var split = new StockSplit
        {
            EquityIssuerId = Issuer,
            PriceSeriesTicker = "IWF",
            EffectiveDate = new DateOnly(2026, 4, 29),
            Numerator = 4m,
            Denominator = 1m,
            PriceAdjustmentAppliedTime = new DateTime(2026, 9, 22, 0, 0, 0, DateTimeKind.Utc),
        };

        HoldingValueBasis
            .TryResolveShareCountFactor(
                ReportDate,
                [split],
                "IVV",
                "AAXJ",
                ["IVV", "IWF"],
                out _,
                TrustSeries
            )
            .Should()
            .BeFalse();
    }

    [Fact]
    public void PresentationFund_SplitOfAnUnlistedFundOfTheTrust_ResolvesWithFactorOne()
    {
        // The primary branch already skips known siblings; a fund no longer in the directory list
        // is still a different series, so it no longer reads as a stale primary symbol.
        HoldingValueBasis
            .TryResolveShareCountFactor(
                ReportDate,
                TrustSplitsAfterReportDate(),
                null,
                "AAXJ",
                [],
                out var factor,
                TrustSeries
            )
            .Should()
            .BeTrue();

        factor.Should().Be(1m);
    }

    [Fact]
    public void Build_TickerClaimedByTwoSeries_IsLeftOut()
    {
        var map = RegisteredSeriesTickers.Build([
            (Issuer, "S000000001", ["DUP", "ONE"]),
            (Issuer, "S000000002", ["dup", "TWO.B"]),
            (Issuer, "", ["IDLESS"]),
        ])[Issuer];

        map.Should().NotContainKey("DUP");
        map["ONE"].Should().Be("S000000001");
        map["TWO-B"].Should().Be("S000000002");
        map.Should().NotContainKey("IDLESS");
    }
}
