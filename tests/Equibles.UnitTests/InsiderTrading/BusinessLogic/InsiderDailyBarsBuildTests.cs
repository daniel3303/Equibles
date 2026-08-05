using Equibles.CorporateActions.Data.Models;
using Equibles.InsiderTrading.BusinessLogic;

namespace Equibles.UnitTests.InsiderTrading.BusinessLogic;

/// <summary>
/// Pins the ONE production path that decides <c>SplitFactorToPresent</c> and
/// <c>SplitBasisAmbiguous</c> for insider price evaluation. The validator tests hand-set those
/// fields on the DTO; these prove real <see cref="StockSplit"/> rows produce them — in
/// particular that a captured-but-unreconciled split (the mixed-basis window) reaches the
/// validator as pending rather than as a guessed factor.
/// </summary>
public class InsiderDailyBarsBuildTests
{
    private static StockSplit Split(
        DateOnly effective,
        decimal numerator,
        decimal denominator,
        string priceSeriesTicker = null,
        bool applied = true
    ) =>
        new()
        {
            EffectiveDate = effective,
            Numerator = numerator,
            Denominator = denominator,
            PriceSeriesTicker = priceSeriesTicker,
            PriceAdjustmentAppliedTime = applied ? DateTime.UtcNow : null,
        };

    private static readonly DateOnly TransactionDate = new(2021, 2, 16);

    [Fact]
    public void Build_AppliedForwardSplitAfterTheTransaction_YieldsTheFactor()
    {
        // AMZN's 20:1, reconciled: close × 20 restates onto the as-filed basis.
        var bar = InsiderDailyBars.Build(
            close: 165.01m,
            low: 163m,
            high: 166.5m,
            TransactionDate,
            [Split(new DateOnly(2022, 6, 6), 20m, 1m, "AMZN")],
            primaryTicker: "AMZN",
            secondaryTickers: []
        );

        bar.SplitBasisAmbiguous.Should().BeFalse();
        bar.SplitFactorToPresent.Should().Be(20m);
        bar.Close.Should().Be(165.01m);
    }

    [Fact]
    public void Build_UnreconciledSplit_MarksTheBasisAmbiguous()
    {
        // Captured split whose price adjustment hasn't run: the stored series straddles two
        // bases, so the evaluation must go pending — never a factor guess.
        var bar = InsiderDailyBars.Build(
            close: 165.01m,
            low: null,
            high: null,
            TransactionDate,
            [Split(new DateOnly(2022, 6, 6), 20m, 1m, "AMZN", applied: false)],
            primaryTicker: "AMZN",
            secondaryTickers: []
        );

        bar.SplitBasisAmbiguous.Should().BeTrue();
    }

    [Fact]
    public void Build_UnattributedLegacySplit_CountsForThePrimarySeries()
    {
        // Insider rows are issuer-level (listedTicker = null → primary), and only the primary
        // series can have produced a pre-attribution legacy row.
        var bar = InsiderDailyBars.Build(
            close: 50m,
            low: null,
            high: null,
            TransactionDate,
            [Split(new DateOnly(2023, 1, 10), 1m, 10m)],
            primaryTicker: "TICK",
            secondaryTickers: []
        );

        bar.SplitBasisAmbiguous.Should().BeFalse();
        bar.SplitFactorToPresent.Should().Be(0.1m, "a 1:10 reverse split divides the count");
    }

    [Fact]
    public void Build_SiblingAttributedSplit_IsSkippedNotApplied()
    {
        // A split belonging to a KNOWN sibling listing moves nothing on the primary basis.
        var bar = InsiderDailyBars.Build(
            close: 50m,
            low: null,
            high: null,
            TransactionDate,
            [Split(new DateOnly(2023, 1, 10), 20m, 1m, "BRK-B")],
            primaryTicker: "BRK-A",
            secondaryTickers: ["BRK-B"]
        );

        bar.SplitBasisAmbiguous.Should().BeFalse();
        bar.SplitFactorToPresent.Should().Be(1m);
    }

    [Fact]
    public void Build_StaleAttributionMatchingNoCurrentListing_MarksAmbiguous()
    {
        // An attribution matching neither the primary nor any secondary is a stale symbol —
        // very likely this series' own split under its old name. Guessing either way risks the
        // ratio-sized error; the basis defers.
        var bar = InsiderDailyBars.Build(
            close: 50m,
            low: null,
            high: null,
            TransactionDate,
            [Split(new DateOnly(2023, 1, 10), 20m, 1m, "OLDNAME")],
            primaryTicker: "NEWNAME",
            secondaryTickers: []
        );

        bar.SplitBasisAmbiguous.Should().BeTrue();
    }

    [Fact]
    public void Build_SplitOnOrBeforeTheTransactionDate_DoesNotMoveTheFactor()
    {
        // A figure dated on the effective date is already post-split (strict comparison,
        // mirroring SplitAdjustment.ShareCountFactor).
        var bar = InsiderDailyBars.Build(
            close: 165.01m,
            low: null,
            high: null,
            TransactionDate,
            [Split(TransactionDate, 20m, 1m, "AMZN")],
            primaryTicker: "AMZN",
            secondaryTickers: []
        );

        bar.SplitBasisAmbiguous.Should().BeFalse();
        bar.SplitFactorToPresent.Should().Be(1m);
    }
}
