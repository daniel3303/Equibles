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
    // Exchange-traded debt — the QVC baby-bond shape.
    [InlineData("6.875% Senior Secured Notes due 2068", ListedSecurityType.DebtSecurities)]
    [InlineData("8.00% Subordinated Debentures due 2032", ListedSecurityType.DebtSecurities)]
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
