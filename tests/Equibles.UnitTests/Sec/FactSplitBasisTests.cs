using Equibles.CorporateActions.Data.Models;
using Equibles.Sec.FinancialFacts.BusinessLogic.Splits;
using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Data.Models;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// One split rule for every fact surface. Statement figures sit on the share basis of their
/// filing date because a post-split filing restates its comparatives; reading an instant count on
/// its period end instead multiplied already-restated counts again (AVGO FY2023 ×10, GOOGL FY2021
/// ×20, TSLA FY2019 ×15). The vectors below are the figures those filings report.
/// </summary>
public class FactSplitBasisTests
{
    private static readonly Guid ListingId = Guid.NewGuid();

    private static StockSplit Split(
        int year,
        int month,
        int day,
        decimal numerator,
        Guid? listingId
    ) =>
        new()
        {
            EffectiveDate = new DateOnly(year, month, day),
            Numerator = numerator,
            Denominator = 1m,
            EquityListingId = listingId,
            PriceSeriesTicker = "X",
        };

    private static FinancialFact Fact(
        decimal value,
        string unit,
        DateOnly periodEnd,
        DateOnly filed
    ) =>
        new()
        {
            Value = value,
            Unit = unit,
            PeriodStart = periodEnd,
            PeriodEnd = periodEnd,
            FiledDate = filed,
        };

    private static FactSplitKind Kind(FactTaxonomy taxonomy, string tag, string unit = "shares") =>
        FactSplitBasis.Classify(taxonomy, tag, unit);

    [Theory]
    [InlineData("USD/shares")]
    [InlineData("CAD/shares")]
    [InlineData("EUR/shares")]
    [InlineData("USD/Shares")]
    [InlineData("USD/share")]
    [InlineData("USD/Share")]
    public void Classify_ShareDenominatedRatio_IsPerShare(string unit)
    {
        Kind(FactTaxonomy.UsGaap, "EarningsPerShareDiluted", unit)
            .Should()
            .Be(FactSplitKind.PerShare);
    }

    // A split moves only a literal share denominator; commodity, currency and inverted ratios
    // stay as filed (shares/USD would even move the other way).
    [Theory]
    [InlineData("USD/bbl")]
    [InlineData("USD/MMBTU")]
    [InlineData("USD/MWh")]
    [InlineData("USD/oz")]
    [InlineData("USD/EUR")]
    [InlineData("USD/item")]
    [InlineData("USD/loan")]
    [InlineData("bbl/D")]
    [InlineData("shares/USD")]
    [InlineData("USD/Shareholder")]
    [InlineData("USD/derivativeShare")]
    [InlineData("USD/shares_unit")]
    [InlineData("MXN/pershare")]
    [InlineData("USD/oneTen-thousandthShare")]
    [InlineData("/shares")]
    [InlineData("USD/shares/shares")]
    [InlineData("USD")]
    [InlineData("pure")]
    [InlineData(null)]
    public void Classify_OtherUnits_AreSplitNeutral(string unit)
    {
        Kind(FactTaxonomy.UsGaap, "EarningsPerShareDiluted", unit).Should().Be(FactSplitKind.None);
    }

    [Theory]
    [InlineData("CommonStockSharesOutstanding")]
    [InlineData("WeightedAverageNumberOfSharesOutstandingBasic")]
    [InlineData("WeightedAverageNumberOfDilutedSharesOutstanding")]
    [InlineData("WeightedAverageNumberOfShareOutstandingBasicAndDiluted")]
    [InlineData("CommonStockSharesIssued")]
    [InlineData("TreasuryStockShares")]
    public void Classify_StatementCommonShareCount_IsStatedOnTheFilingBasis(string tag)
    {
        Kind(FactTaxonomy.UsGaap, tag).Should().Be(FactSplitKind.StatedShareCount);
    }

    [Fact]
    public void Classify_IfrsWeightedAverageShares_IsStatedOnTheFilingBasis()
    {
        Kind(FactTaxonomy.IfrsFull, "WeightedAverageShares")
            .Should()
            .Be(FactSplitKind.StatedShareCount);
    }

    [Fact]
    public void Classify_CoverPageCount_IsObservedOnItsAsOfDate()
    {
        Kind(FactTaxonomy.Dei, "EntityCommonStockSharesOutstanding")
            .Should()
            .Be(FactSplitKind.ObservedShareCount);
    }

    [Theory]
    [InlineData("PreferredStockSharesAuthorized")]
    [InlineData("CommonStockSharesAuthorized")]
    [InlineData("PreferredStockSharesOutstanding")]
    public void Classify_ShareQuantityASplitDoesNotMove_StaysAsFiled(string tag)
    {
        Kind(FactTaxonomy.UsGaap, tag).Should().Be(FactSplitKind.None);
    }

    [Fact]
    public void Classify_ShareCountWithoutItsConcept_Throws()
    {
        var classify = () => FactSplitBasis.Classify(null, "shares");

        classify.Should().Throw<ArgumentException>();
        FactSplitBasis.Classify(null, "USD/shares").Should().Be(FactSplitKind.PerShare);
        FactSplitBasis.Classify(null, "USD").Should().Be(FactSplitKind.None);
    }

    [Theory]
    [InlineData("shares-outstanding", "shares", FactSplitKind.StatedShareCount)]
    [InlineData("weighted-average-shares-diluted", "shares", FactSplitKind.StatedShareCount)]
    [InlineData("eps-diluted", "EUR/shares", FactSplitKind.PerShare)]
    [InlineData("revenue", "EUR", FactSplitKind.None)]
    [InlineData("treasury-stock", "shares", FactSplitKind.None)]
    public void ClassifyStatementFigure_UsesTheAlias(string alias, string unit, FactSplitKind kind)
    {
        FactSplitBasis.ClassifyStatementFigure(alias, unit).Should().Be(kind);
    }

    [Fact]
    public void Restate_TslaFy2019FromTheFy2020TenK_AppliesOnlyTheLaterSplit()
    {
        // The FY2020 10-K (filed 2021-02-08) already states FY2019 after the 2020 5:1 split.
        var splits = new[] { Split(2020, 8, 31, 5m, ListingId), Split(2022, 8, 25, 3m, ListingId) };
        var fact = Fact(
            905_000_000m,
            "shares",
            new DateOnly(2019, 12, 31),
            new DateOnly(2021, 2, 8)
        );

        var restated = FactSplitBasis.Restate(
            fact,
            FactSplitKind.StatedShareCount,
            splits,
            ListingId
        );

        restated.Value.Should().Be(2_715_000_000m);
        restated.Adjusted.Should().BeTrue();
    }

    [Fact]
    public void Restate_AvgoFy2023_OriginalAndRestatedFilingsAgree()
    {
        var splits = new[] { Split(2024, 7, 15, 10m, ListingId) };
        var yearEnd = new DateOnly(2023, 10, 29);
        var original = Fact(413_900_000m, "shares", yearEnd, new DateOnly(2023, 12, 14));
        var restatedByFy2024 = Fact(4_139_000_000m, "shares", yearEnd, new DateOnly(2024, 12, 20));

        FactSplitBasis
            .Restate(original, FactSplitKind.StatedShareCount, splits, ListingId)
            .Value.Should()
            .Be(4_139_000_000m);
        var later = FactSplitBasis.Restate(
            restatedByFy2024,
            FactSplitKind.StatedShareCount,
            splits,
            ListingId
        );
        later.Value.Should().Be(4_139_000_000m);
        later.Adjusted.Should().BeFalse();
    }

    [Fact]
    public void Restate_GooglFy2021FromTheFy2022TenK_IsAlreadyOnTodaysBasis()
    {
        var splits = new[] { Split(2022, 7, 18, 20m, ListingId) };
        var fact = Fact(
            13_242_000_000m,
            "shares",
            new DateOnly(2021, 12, 31),
            new DateOnly(2023, 2, 3)
        );

        FactSplitBasis
            .Restate(fact, FactSplitKind.StatedShareCount, splits, ListingId)
            .Value.Should()
            .Be(13_242_000_000m);
    }

    [Fact]
    public void Restate_CoverCountTakenBeforeASplitAndFiledAfterIt_UsesTheAsOfDate()
    {
        var splits = new[] { Split(2024, 6, 10, 10m, ListingId) };
        var fact = Fact(
            246_000_000m,
            "shares",
            new DateOnly(2024, 6, 5),
            new DateOnly(2024, 6, 14)
        );

        FactSplitBasis
            .Restate(fact, FactSplitKind.ObservedShareCount, splits, ListingId)
            .Value.Should()
            .Be(2_460_000_000m);
        FactSplitBasis
            .Restate(fact, FactSplitKind.StatedShareCount, splits, ListingId)
            .Value.Should()
            .Be(246_000_000m);
    }

    [Fact]
    public void Restate_PerShareFiledBeforeTheSplit_Divides()
    {
        var splits = new[] { Split(2024, 7, 15, 10m, ListingId) };
        var fact = Fact(
            39.81m,
            "USD/shares",
            new DateOnly(2023, 10, 29),
            new DateOnly(2023, 12, 14)
        );

        FactSplitBasis
            .Restate(fact, FactSplitKind.PerShare, splits, ListingId)
            .Value.Should()
            .Be(3.981m);
    }

    [Fact]
    public void Restate_SplitNeutralFact_IsNeverMoved()
    {
        var splits = new[] { Split(2024, 7, 15, 10m, ListingId) };
        var fact = Fact(
            100_000_000m,
            "shares",
            new DateOnly(2023, 10, 29),
            new DateOnly(2023, 12, 14)
        );

        var restated = FactSplitBasis.Restate(fact, FactSplitKind.None, splits, ListingId);

        restated.Value.Should().Be(100_000_000m);
        restated.IsSplitSensitive.Should().BeFalse();
    }

    [Fact]
    public void Restate_UnattributedSplitAfterTheBasisDate_KeepsTheFiledValueAsUnresolved()
    {
        var splits = new[] { Split(2024, 7, 15, 10m, listingId: null) };
        var fact = Fact(
            413_900_000m,
            "shares",
            new DateOnly(2023, 10, 29),
            new DateOnly(2023, 12, 14)
        );

        var restated = FactSplitBasis.Restate(
            fact,
            FactSplitKind.StatedShareCount,
            splits,
            ListingId
        );

        restated.Value.Should().Be(413_900_000m);
        restated.Unresolved.Should().BeTrue();
        restated.Adjusted.Should().BeFalse();
    }

    [Theory]
    [InlineData("unattributed")]
    [InlineData("invalid")]
    [InlineData("duplicate")]
    [InlineData("other-listing")]
    [InlineData("renamed")]
    public void Restate_NativeAttribution_PreservesUnresolvedValuesAndIsolatesOtherListings(
        string scope
    )
    {
        var split = new StockSplit
        {
            EquityListingId =
                scope == "unattributed" ? null
                : scope == "other-listing" ? Guid.NewGuid()
                : ListingId,
            PriceSeriesTicker = "OLD-SOURCE-SYMBOL",
            EffectiveDate = new DateOnly(2022, 7, 18),
            Numerator = scope == "invalid" ? 0 : 10m,
            Denominator = 1m,
        };
        var splits = new List<StockSplit> { split };
        if (scope == "duplicate")
            splits.Add(split);
        var fact = Fact(100m, "USD/shares", new DateOnly(2019, 12, 31), new DateOnly(2020, 1, 2));

        var restated = FactSplitBasis.Restate(fact, FactSplitKind.PerShare, splits, ListingId);

        restated.Unresolved.Should().Be(scope is "unattributed" or "invalid" or "duplicate");
        restated.Adjusted.Should().Be(scope == "renamed");
        restated.Value.Should().Be(scope == "renamed" ? 10m : 100m);
    }

    [Fact]
    public void Restate_AnotherListingsSplit_DoesNotMoveThisListing()
    {
        var splits = new[] { Split(2024, 7, 15, 10m, Guid.NewGuid()) };
        var fact = Fact(
            413_900_000m,
            "shares",
            new DateOnly(2023, 10, 29),
            new DateOnly(2023, 12, 14)
        );

        var restated = FactSplitBasis.Restate(
            fact,
            FactSplitKind.StatedShareCount,
            splits,
            ListingId
        );

        restated.Value.Should().Be(413_900_000m);
        restated.Unresolved.Should().BeFalse();
    }
}
