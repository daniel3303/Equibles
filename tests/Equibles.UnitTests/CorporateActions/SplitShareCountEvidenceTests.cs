using Equibles.CorporateActions.Data;
using Equibles.CorporateActions.Data.Models;

namespace Equibles.UnitTests.CorporateActions;

/// <summary>
/// A split changes the issuer's share count; a price factor does not. Honeywell's US series
/// carries two spin-off factors (1061:1000, 1907:2000) while its real 1:2 sits on the Milan
/// listing; the cover-page counts around each date tell them apart.
/// </summary>
public class SplitShareCountEvidenceTests
{
    private static readonly Guid UsListing = Guid.NewGuid();
    private static readonly Guid MilanListing = Guid.NewGuid();

    private static StockSplit Split(
        DateOnly effective,
        decimal numerator,
        decimal denominator,
        Guid? listingId = null,
        StockSplitSource source = StockSplitSource.Yahoo
    ) =>
        new()
        {
            Id = Guid.NewGuid(),
            EffectiveDate = effective,
            Numerator = numerator,
            Denominator = denominator,
            EquityListingId = listingId ?? UsListing,
            Source = source,
        };

    private static ShareCountObservation Count(DateOnly asOf, DateOnly filed, decimal shares) =>
        new(asOf, filed, shares);

    private static readonly ShareCountObservation[] HoneywellCounts =
    [
        Count(new DateOnly(2025, 9, 30), new DateOnly(2025, 10, 23), 634_887_208m),
        Count(new DateOnly(2026, 1, 23), new DateOnly(2026, 2, 17), 635_675_701m),
        Count(new DateOnly(2026, 3, 31), new DateOnly(2026, 4, 23), 633_653_119m),
        Count(new DateOnly(2026, 6, 30), new DateOnly(2026, 7, 23), 316_940_010m),
    ];

    private static StockSplitShareCountEvidence? EvidenceOf(
        IEnumerable<SplitShareCountVerdict> verdicts,
        StockSplit split
    ) => verdicts.Single(verdict => verdict.Split == split).Evidence;

    [Fact]
    public void Evaluate_Honeywell_ConfirmsTheMilanHalvingAndContradictsBothSpinOffFactors()
    {
        var spinOff = Split(new DateOnly(2025, 10, 30), 1061m, 1000m);
        var usFactor = Split(new DateOnly(2026, 6, 29), 1907m, 2000m);
        var milan = Split(new DateOnly(2026, 6, 29), 1m, 2m, MilanListing);

        var verdicts = SplitShareCountEvidence.Evaluate(
            [spinOff, usFactor, milan],
            HoneywellCounts
        );

        EvidenceOf(verdicts, spinOff).Should().Be(StockSplitShareCountEvidence.Contradicted);
        EvidenceOf(verdicts, usFactor).Should().Be(StockSplitShareCountEvidence.Contradicted);
        EvidenceOf(verdicts, milan).Should().Be(StockSplitShareCountEvidence.Confirmed);
        verdicts
            .Single(verdict => verdict.Split == milan)
            .AsOf.Should()
            .Be(new DateOnly(2026, 6, 30));
    }

    [Fact]
    public void Evaluate_ACountFiledAfterTheSplit_IsNotThePreSplitWitness()
    {
        // Mueller's 10-Q cover was dated the day before its 2:1 but already stated the doubled count.
        var split = Split(new DateOnly(2023, 10, 23), 2m, 1m);
        var counts = new[]
        {
            Count(new DateOnly(2023, 7, 21), new DateOnly(2023, 7, 26), 56_778_790m),
            Count(new DateOnly(2023, 10, 22), new DateOnly(2023, 10, 25), 113_529_410m),
            Count(new DateOnly(2024, 2, 22), new DateOnly(2024, 2, 28), 113_634_944m),
        };

        var verdict = SplitShareCountEvidence.Evaluate([split], counts).Single();

        verdict.Evidence.Should().Be(StockSplitShareCountEvidence.Confirmed);
        verdict.AsOf.Should().Be(new DateOnly(2024, 2, 22));
    }

    [Fact]
    public void Evaluate_CountsThatMovedByNeitherOneNorTheRatio_DecideNothing()
    {
        // A reverse split at a dilutive issuer: the count fell, but not by the ratio.
        var split = Split(new DateOnly(2026, 7, 27), 1m, 8m);
        var counts = new[]
        {
            Count(new DateOnly(2026, 5, 10), new DateOnly(2026, 5, 14), 80_000_000m),
            Count(new DateOnly(2026, 8, 10), new DateOnly(2026, 8, 13), 28_300_000m),
        };

        var verdict = SplitShareCountEvidence.Evaluate([split], counts).Single();

        verdict.Evidence.Should().BeNull();
        verdict.AsOf.Should().BeNull();
    }

    [Fact]
    public void Evaluate_AnotherEventBetweenTheWitnesses_DecidesNeither()
    {
        // A 2:1 then a 1:2 leave the count unchanged; neither is a price factor.
        var forward = Split(new DateOnly(2024, 3, 1), 2m, 1m);
        var reverse = Split(new DateOnly(2024, 4, 1), 1m, 2m);
        var counts = new[]
        {
            Count(new DateOnly(2024, 1, 31), new DateOnly(2024, 2, 5), 100m),
            Count(new DateOnly(2024, 5, 1), new DateOnly(2024, 5, 6), 100m),
        };

        var verdicts = SplitShareCountEvidence.Evaluate([forward, reverse], counts);

        verdicts.Should().OnlyContain(verdict => verdict.Evidence == null);
    }

    [Fact]
    public void Evaluate_ARatioOnTheBasisOfOne_CannotBeToldFromNoChange()
    {
        var stockDividend = Split(new DateOnly(2026, 5, 13), 101m, 100m);
        var counts = new[]
        {
            Count(new DateOnly(2026, 4, 20), new DateOnly(2026, 4, 28), 800_000_000m),
            Count(new DateOnly(2026, 7, 20), new DateOnly(2026, 7, 28), 808_000_000m),
        };

        SplitShareCountEvidence
            .Evaluate([stockDividend], counts)
            .Single()
            .Evidence.Should()
            .BeNull();
    }

    [Fact]
    public void Evaluate_WithoutAWitnessOnBothSides_DecidesNothing()
    {
        var split = Split(new DateOnly(2026, 9, 1), 4m, 1m);
        var counts = new[] { Count(new DateOnly(2026, 7, 20), new DateOnly(2026, 7, 28), 100m) };

        SplitShareCountEvidence.Evaluate([split], counts).Single().Evidence.Should().BeNull();
    }

    [Theory]
    [InlineData(StockSplitSource.Manual)]
    [InlineData(StockSplitSource.SecFiling)]
    public void Evaluate_AnAuthoritativeRatio_StatesTheEventWithoutCounts(StockSplitSource source)
    {
        var stated = Split(new DateOnly(2014, 6, 9), 7m, 1m, source: source);
        var agreeing = Split(new DateOnly(2014, 6, 9), 7m, 1m, MilanListing);
        var other = Split(new DateOnly(2014, 6, 12), 1061m, 1000m, MilanListing);

        var verdicts = SplitShareCountEvidence.Evaluate([stated, agreeing, other], []);

        EvidenceOf(verdicts, stated).Should().Be(StockSplitShareCountEvidence.Confirmed);
        EvidenceOf(verdicts, agreeing).Should().Be(StockSplitShareCountEvidence.Confirmed);
        EvidenceOf(verdicts, other).Should().Be(StockSplitShareCountEvidence.Contradicted);
    }
}
