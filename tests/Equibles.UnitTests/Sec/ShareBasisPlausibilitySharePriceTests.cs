using Equibles.Sec.FinancialFacts.BusinessLogic;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// Pins the listed-security-basis test the financial-facts importer gates its unit-mismatch guard
/// on. The guard must protect a stored (market cap, shares) pair the Yahoo importer maintains on
/// the listed-security basis (implied per-share price is a real quote), while still letting the
/// EDGAR cover-page count repair stored garbage: a nominal 1/100/1000-share placeholder implies
/// millions per share, a garbage-large count implies fractions of a cent — neither is a price a
/// listed security trades at, so neither deserves protection.
/// </summary>
public class ShareBasisPlausibilitySharePriceTests
{
    [Fact]
    public void ImpliesPlausibleSharePrice_ListedSecurityPair_True()
    {
        // AKTX's healed pair: ~$27M cap over ~2.5M ADSs implies ~$10.90 — a real quote.
        ShareBasisPlausibility
            .ImpliesPlausibleSharePrice(27_000_000d, 2_477_000L)
            .Should()
            .BeTrue();
    }

    [Fact]
    public void ImpliesPlausibleSharePrice_MostExpensiveRealListing_True()
    {
        // BRK.A trades around $780k/share — the extreme real quote must stay inside the band.
        ShareBasisPlausibility
            .ImpliesPlausibleSharePrice(1_140_000_000_000d, 1_460_000L)
            .Should()
            .BeTrue();
    }

    [Fact]
    public void ImpliesPlausibleSharePrice_NominalPlaceholderCount_False()
    {
        // A shell pinned to a 1-share cover-page placeholder implies $2B per share; the stored
        // count is garbage and must remain repairable by the EDGAR figure.
        ShareBasisPlausibility.ImpliesPlausibleSharePrice(2_000_000_000d, 1L).Should().BeFalse();
    }

    [Fact]
    public void ImpliesPlausibleSharePrice_GarbageLargeCount_False()
    {
        // A garbage-large stored count against a sane cap implies fractions of a cent per share;
        // it is not on the listed-security basis and must remain repairable.
        ShareBasisPlausibility
            .ImpliesPlausibleSharePrice(27_000_000d, 91_567_009_533L)
            .Should()
            .BeFalse();
    }

    [Theory]
    [InlineData(0d, 1_000_000L)]
    [InlineData(-1d, 1_000_000L)]
    [InlineData(1_000_000d, 0L)]
    [InlineData(1_000_000d, -1L)]
    public void ImpliesPlausibleSharePrice_MissingFigure_False(double marketCap, long shares)
    {
        // With either figure missing there is no evidence the stored pair is on the listed
        // basis, so it gets no protection and the EDGAR write proceeds.
        ShareBasisPlausibility.ImpliesPlausibleSharePrice(marketCap, shares).Should().BeFalse();
    }
}
