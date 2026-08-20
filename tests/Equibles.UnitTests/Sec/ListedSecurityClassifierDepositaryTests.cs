using Equibles.Sec.FinancialFacts.BusinessLogic;
using FluentAssertions;
using Xunit;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// Pins <see cref="ListedSecurityClassifier.IsAmericanDepositary"/>, which answers what UNIT a
/// listing is quoted in — a different question from <c>Classify</c>'s what KIND of security it is
/// (an ADS over ordinary shares is common equity and classifies as such). Both writers of
/// <c>CommonStock.SharesOutStanding</c> ask it before mixing an EDGAR cover-page count with a
/// price feed's share base: the cover page counts ordinary shares while the price is per ADS, and
/// the deposit ratio between them (13x for ONC, 2x for AZN/SNY, 2,160x for ARBK) is small enough
/// to sit inside <c>ShareBasisPlausibility</c>'s same-unit tolerance, so nothing but the issuer's
/// own registered title can catch it.
///
/// Every title below is a verbatim <c>dei:Security12bTitle</c> read from the production store, so
/// the shapes are the ones filers actually register rather than the ones a pattern was written
/// for.
/// </summary>
public class ListedSecurityClassifierDepositaryTests
{
    [Theory]
    // The three that motivated the guard: all list ADSs while filing domestic forms, so the
    // form-based foreign-private-issuer check never sees them.
    [InlineData(
        "American Depositary Shares, each representing 13 Ordinary Shares, par value $0.0001 per share"
    )]
    [InlineData(
        "American Depositary Shares, each representing one half of an Ordinary Share of 25¢ each"
    )]
    [InlineData(
        "American Depositary Shares, each representing one half of one ordinary share, par value €2 per share"
    )]
    // Singular "Share", and the bare title with no ratio clause at all.
    [InlineData(
        "American Depositary Share representing 100 Series B Shares, par value 50 Rupiah per share"
    )]
    [InlineData("American Depositary Shares")]
    // Receipts rather than shares.
    [InlineData("American Depositary Receipts, each representing one B Share")]
    // The issuer's own misspelling. "Depository" is not the canonical word, but it is the title
    // THEY registered and nothing upstream normalizes it.
    [InlineData("American Depository Shares, each representing 2,160 ordinary shares")]
    [InlineData("American depository shares (the “ADSs”), each of which represents one share")]
    // Case and quoting vary freely across filers.
    [InlineData(
        "American depositary shares, each ADS represents 15 ordinary shares, par valueUS$0.001 per share"
    )]
    [InlineData("American Depositary Shares (“ADS”), each representing 10 ordinary shares")]
    [InlineData("American Depositary Shares (ADSs)")]
    // The phrase inside a parenthetical, which Classify strips before choosing a kind — matching
    // the RAW title is what keeps this one visible.
    [InlineData(
        "American Depositary Shares (as evidenced by American Depositary Receipts), each representing one ordinary share"
    )]
    public void IsAmericanDepositary_RegisteredAdsTitle_IsTrue(string title)
    {
        ListedSecurityClassifier.IsAmericanDepositary(title).Should().BeTrue();
    }

    [Theory]
    // Ordinary domestic equity: the cover page and the listing count the same unit.
    [InlineData("Common Stock, $0.001 par value per share")]
    [InlineData("Common stock")]
    [InlineData("Class A Ordinary Shares")]
    // The far commoner depositary form, and the one that must NOT fire: a fractional interest in
    // the issuer's OWN preferred stock. Treating it as a foreign-unit mismatch would throw away
    // the authoritative EDGAR count for every bank that lists preferred this way.
    [InlineData(
        "Depositary Shares, each representing a 1/1,000th interest in a share of 7.375% Fixed-Rate Non-Cumulative Preferred Stock, Series D"
    )]
    // A Global Depositary Share carries the same unit mismatch but is deliberately out of scope —
    // every GDS issuer on record files as a foreign private issuer, so the form-based guard
    // answers for them first.
    [InlineData("Global Depositary Shares")]
    // Exchange-traded debt from an ADS issuer: same company, different listed security, and the
    // share-count question does not arise.
    [InlineData("4.875% Notes due 2033")]
    // Word-boundary sanity: neither the adjective nor a longer word containing it counts.
    [InlineData("American Insurance Common Stock")]
    [InlineData("Depositary")]
    public void IsAmericanDepositary_OtherTitle_IsFalse(string title)
    {
        ListedSecurityClassifier.IsAmericanDepositary(title).Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void IsAmericanDepositary_MissingTitle_IsFalse(string title)
    {
        // Absence of a registered title is not evidence of a depositary listing. Stocks whose
        // 12(b) title was never materialized must keep their existing behaviour rather than
        // silently lose the authoritative EDGAR count.
        ListedSecurityClassifier.IsAmericanDepositary(title).Should().BeFalse();
    }

    [Fact]
    public void IsAmericanDepositary_AdsTitle_StillClassifiesAsCommonEquity()
    {
        // The two questions are independent and both answers matter: the listing is common equity
        // (so it belongs in every equity surface) AND it is quoted per receipt (so its share count
        // must not be mixed with the cover page's). A future change that made the ADS test a
        // Classify branch would silently drop these companies out of the equity universe.
        const string title =
            "American Depositary Shares, each representing 13 Ordinary Shares, par value $0.0001 per share";

        ListedSecurityClassifier.IsAmericanDepositary(title).Should().BeTrue();
        ListedSecurityClassifier
            .Classify(title)
            .Should()
            .Be(Equibles.CommonStocks.Data.Models.ListedSecurityType.CommonShares);
    }
}
