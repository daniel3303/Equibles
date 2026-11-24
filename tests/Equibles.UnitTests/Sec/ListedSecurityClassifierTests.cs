using Equibles.CommonStocks.Data.Models;
using Equibles.Sec.FinancialFacts.BusinessLogic;
using FluentAssertions;
using Xunit;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// Pins the 12(b)-title classifier on real cover-page titles. The earliest
/// keyword in the title decides the kind — a title leads with the security's
/// own noun and then describes what it bundles, converts into, or carries as a
/// rider — so a SPAC unit that mentions warrants is a unit, a warrant over MLP
/// units is a warrant, and common stock with a poison-pill rights rider stays
/// common. Trailing-head-noun purchase forms ("Common Stock Purchase
/// Warrants") classify as the wrapper, while the same phrase in rider language
/// stays with the security it rides on. Nothing here reads tickers or company
/// names; the input is always the authoritative dei:Security12bTitle text.
/// </summary>
public class ListedSecurityClassifierTests
{
    [Theory]
    // Plain equity shapes.
    [InlineData("Common Stock, par value $0.01 per share", ListedSecurityType.CommonShares)]
    [InlineData("Class A Common Stock", ListedSecurityType.CommonShares)]
    [InlineData("Class A Ordinary Shares", ListedSecurityType.CommonShares)]
    [InlineData("Common Shares of Beneficial Interest", ListedSecurityType.CommonShares)]
    [InlineData("Shares of Beneficial Interest", ListedSecurityType.CommonShares)]
    [InlineData(
        "American Depositary Shares, each representing two Class A ordinary shares",
        ListedSecurityType.CommonShares
    )]
    // The poison-pill rider: the rights are attached to the common, not listed.
    [InlineData(
        "Common Stock, par value $0.01 per share (and associated Preferred Share Purchase Rights)",
        ListedSecurityType.CommonShares
    )]
    // Preferred, including the depositary-receipt form (bare "Depositary
    // Shares" is deliberately not a common-equity keyword).
    [InlineData(
        "7.00% Series B Cumulative Redeemable Perpetual Preferred Stock",
        ListedSecurityType.PreferredShares
    )]
    [InlineData(
        "Depositary Shares, each representing a 1/1,000th interest in a share of Series A Preferred Stock",
        ListedSecurityType.PreferredShares
    )]
    // Preferred named only inside the parenthetical (US Bancorp, Huntington), run into the word
    // beside it (Customers Bancorp, M&T), or abbreviated (Public Storage, Citigroup, KeyCorp).
    [InlineData(
        "Depositary Shares (each representing 1/1,000th interest in a share of Series B Non-Cumulative Perpetual Preferred Stock, par value $1.00)",
        ListedSecurityType.PreferredShares
    )]
    [InlineData(
        "Fixed-to-Floating Rate Non-Cumulative PerpetualPreferred Stock, Series C, par value $1.00 per share",
        ListedSecurityType.PreferredShares
    )]
    [InlineData(
        "8.00% Series B Cumulative Redeemable PreferredShares of Beneficial Interest, $0.01 Par Value",
        ListedSecurityType.PreferredShares
    )]
    [InlineData(
        "Depositary Shares Each Representing 1/1,000 of a 3.875% Cum Pref Share, Series N, $0.01 par value",
        ListedSecurityType.PreferredShares
    )]
    [InlineData(
        "6.5% Series C Fixed-to-Floating Rate Cum Redeem Pfd Stk",
        ListedSecurityType.PreferredShares
    )]
    [InlineData(
        "7.625% TRUPs of Cap III (and registrant’s guaranty)",
        ListedSecurityType.PreferredShares
    )]
    [InlineData(
        "Depositary Shares (each representing the right to receive one preferred share)",
        ListedSecurityType.PreferredShares
    )]
    // A depositary share that names nothing stays unplaced, as does one over ordinary shares and an
    // American or global one whose kind only its parenthetical names; a rider never names the security.
    [InlineData(
        "Depositary Shares (each representing the right to receive one ordinary share)",
        ListedSecurityType.Other
    )]
    [InlineData(
        "Depositary Shares (each representing one ordinary share convertible into preferred shares)",
        ListedSecurityType.Other
    )]
    [InlineData(
        "Depositary Shares, each representing one Class A Share (and associated Preferred Share Purchase Rights)",
        ListedSecurityType.Other
    )]
    [InlineData(
        "Depositary Shares, each representing one Class A Share (convertible into preferred shares)",
        ListedSecurityType.Other
    )]
    [InlineData(
        "Depositary Shares (each representing one Class A Share, and associated preferred shares)",
        ListedSecurityType.Other
    )]
    [InlineData(
        "Depositary Shares (with Preferred Share Purchase Rights)",
        ListedSecurityType.Other
    )]
    [InlineData("Depositary Shares", ListedSecurityType.Other)]
    [InlineData(
        "Global Depositary Shares (each representing one preferred share)",
        ListedSecurityType.Other
    )]
    [InlineData(
        "Class A Shares (and associated Preferred Share Purchase Rights)",
        ListedSecurityType.Other
    )]
    [InlineData(
        "American Depository Shares (each representing one preferred share)",
        ListedSecurityType.Other
    )]
    // Debt that names itself as securities rather than notes (Lloyds, Santander, Vodafone).
    [InlineData(
        "$1,000,000,000 4.500% Subordinated Securities due 2024",
        ListedSecurityType.DebtSecurities
    )]
    [InlineData(
        "3.500% Second Ranking Senior Debt Securities due 2022",
        ListedSecurityType.DebtSecurities
    )]
    [InlineData("NC10 Capital Securities due 2081", ListedSecurityType.DebtSecurities)]
    // Near misses: a fund whose name mentions debt, and letters inside a word.
    [InlineData("Debt Strategies Fund Common Stock", ListedSecurityType.CommonShares)]
    [InlineData("Prefab Holdings Capital Stock", ListedSecurityType.Other)]
    // Exchange-traded debt — the QVC baby-bond shape.
    [InlineData("6.875% Senior Secured Notes due 2068", ListedSecurityType.DebtSecurities)]
    [InlineData("8.00% Subordinated Debentures due 2032", ListedSecurityType.DebtSecurities)]
    // Exchange-traded notes: the filed abbreviation is the head noun, and a "Shares" inside the
    // product name is not a common-equity keyword.
    [InlineData(
        "Credit Suisse X-Links® Gold Shares Covered Call ETNs due February 2, 2033",
        ListedSecurityType.DebtSecurities
    )]
    [InlineData("iPath® Select MLP ETN", ListedSecurityType.DebtSecurities)]
    [InlineData(
        "DB Gold Double Long Exchange Traded Notes due February 15, 2038",
        ListedSecurityType.DebtSecurities
    )]
    // Near misses: the letters inside a word, and equity titles that only mention exchanges.
    [InlineData("Vietnam Enterprise Investments Ordinary Shares", ListedSecurityType.CommonShares)]
    [InlineData(
        "Common Shares of Beneficial Interest of an Exchange Traded Fund",
        ListedSecurityType.CommonShares
    )]
    [InlineData("Betnet Holdings Capital Stock", ListedSecurityType.Other)]
    // Wrappers lead with their own noun whatever they embed.
    [InlineData(
        "Units, each consisting of one share of Class A Common Stock and one-half of one redeemable Warrant",
        ListedSecurityType.Units
    )]
    [InlineData("Warrants to purchase Common Units", ListedSecurityType.Warrants)]
    [InlineData(
        "Redeemable warrants, each whole warrant exercisable for one share of Class A common stock",
        ListedSecurityType.Warrants
    )]
    [InlineData("Subscription Rights to purchase Common Stock", ListedSecurityType.Rights)]
    // Trailing-head-noun form: "<underlier> Purchase Warrants/Rights" IS the
    // wrapper — the real 12(b) shape for standalone listed warrants and
    // poison-pill rights registered in their own row.
    [InlineData("Common Stock Purchase Warrants", ListedSecurityType.Warrants)]
    [InlineData("Class A Common Stock Purchase Warrants", ListedSecurityType.Warrants)]
    [InlineData("Preferred Share Purchase Rights", ListedSecurityType.Rights)]
    // …but the same phrase inside rider language stays with the security it
    // rides on, parenthesized or not — classifying the rider would exclude
    // genuine common stock.
    [InlineData(
        "Common Stock, par value $0.01 per share, together with the associated Preferred Stock Purchase Rights",
        ListedSecurityType.CommonShares
    )]
    [InlineData(
        "Common Stock, $0.01 par value including the Preferred Stock Purchase Rights",
        ListedSecurityType.CommonShares
    )]
    [InlineData(
        "Shares of common stock, $0.01 par value per share,including the preferred stock purchase rights",
        ListedSecurityType.CommonShares
    )]
    // A rights listing in its own row still classifies Rights.
    [InlineData("Rights to purchase Common Shares, no par value", ListedSecurityType.Rights)]
    // MLP and fund units both classify Units; surfaces deliberately keep them.
    [InlineData("Common Units Representing Limited Partner Interests", ListedSecurityType.Units)]
    [InlineData("Units Representing Limited Partnership Interests", ListedSecurityType.Units)]
    // Word boundaries: "United" is not a unit, "brights" is not a right.
    [InlineData("United Insurance Capital Stock", ListedSecurityType.Other)]
    // Unrecognized titles are Other — treated like Unknown, never excluded.
    [InlineData("Purchase Contracts", ListedSecurityType.Other)]
    public void Classify_Title_MapsToKind(string title, ListedSecurityType expected)
    {
        ListedSecurityClassifier.Classify(title).Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Classify_MissingTitle_IsUnknown(string title)
    {
        ListedSecurityClassifier.Classify(title).Should().Be(ListedSecurityType.Unknown);
    }
}
