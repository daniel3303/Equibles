using Equibles.CommonStocks.Data.Models;
using Equibles.Sec.HostedService.Services;
using Xunit;

namespace Equibles.UnitTests.Sec;

public class UsPresentationTickerTests
{
    private static Dictionary<string, ListedSecurityType> Filed(
        params (string Symbol, ListedSecurityType Type)[] rows
    ) => rows.ToDictionary(row => row.Symbol, row => row.Type);

    [Fact]
    public void FiledWarrantListedFirst_PresentsFiledCommonSibling()
    {
        var chosen = UsPresentationTicker.Choose(
            ["BEATW", "BEAT"],
            Filed(("BEATW", ListedSecurityType.Warrants), ("BEAT", ListedSecurityType.CommonShares))
        );

        Assert.Equal("BEAT", chosen);
    }

    [Fact]
    public void SpacUnitListedFirst_PresentsFirstFiledCommonInSecOrder()
    {
        var chosen = UsPresentationTicker.Choose(
            ["SIMAU", "SIMAW", "SIMA"],
            Filed(
                ("SIMAU", ListedSecurityType.Units),
                ("SIMAW", ListedSecurityType.Warrants),
                ("SIMA", ListedSecurityType.CommonShares)
            )
        );

        Assert.Equal("SIMA", chosen);
    }

    [Fact]
    public void ClassSeparatorsMatchTheNormalizedFiledSymbol()
    {
        var chosen = UsPresentationTicker.Choose(
            ["SRG-PA", "SRG"],
            Filed(
                ("SRGPA", ListedSecurityType.PreferredShares),
                ("SRG", ListedSecurityType.CommonShares)
            )
        );

        Assert.Equal("SRG", chosen);
    }

    [Fact]
    public void MlpUnitsWithoutACommonSibling_KeepSecOrder()
    {
        // EPD files only its common units; an unclassified sibling must never displace it.
        var chosen = UsPresentationTicker.Choose(
            ["EPD", "EPDU"],
            Filed(("EPD", ListedSecurityType.Units))
        );

        Assert.Equal("EPD", chosen);
    }

    [Fact]
    public void UnclassifiedFirstTicker_KeepsSecOrderEvenWithAFiledCommonSibling()
    {
        // A stale registration (a pre-bankruptcy symbol) is not evidence against SEC's current order.
        var chosen = UsPresentationTicker.Choose(
            ["SNYRQ", "SNYR"],
            Filed(("SNYR", ListedSecurityType.CommonShares))
        );

        Assert.Equal("SNYRQ", chosen);
    }

    [Fact]
    public void TwoCommonClasses_KeepSecOrder()
    {
        var chosen = UsPresentationTicker.Choose(
            ["BF-B", "BF-A"],
            Filed(
                ("BFA", ListedSecurityType.CommonShares),
                ("BFB", ListedSecurityType.CommonShares)
            )
        );

        Assert.Equal("BF-B", chosen);
    }

    [Fact]
    public void FiledDebtListedFirst_PresentsUnclassifiedSiblingOverLaterDebt()
    {
        // BIP's units are filed under a symbol that matches no listing, so they stay unclassified.
        var chosen = UsPresentationTicker.Choose(
            ["BIPH", "BIPI", "BIP", "BIPJ"],
            Filed(
                ("BIPH", ListedSecurityType.DebtSecurities),
                ("BIPI", ListedSecurityType.DebtSecurities),
                ("BIPJ", ListedSecurityType.DebtSecurities)
            )
        );

        Assert.Equal("BIP", chosen);
    }

    [Fact]
    public void FiledPreferredListedFirst_PresentsUnclassifiedSibling()
    {
        var chosen = UsPresentationTicker.Choose(
            ["ABC-PA", "ABC"],
            Filed(("ABCPA", ListedSecurityType.PreferredShares))
        );

        Assert.Equal("ABC", chosen);
    }

    [Fact]
    public void FiledWarrantListedFirst_PresentsUnclassifiedSibling()
    {
        var chosen = UsPresentationTicker.Choose(
            ["ABCW", "ABC"],
            Filed(("ABCW", ListedSecurityType.Warrants))
        );

        Assert.Equal("ABC", chosen);
    }

    [Fact]
    public void FiledRightsListedFirst_PresentsFiledUnitsOrOtherSibling()
    {
        Assert.Equal(
            "ABCU",
            UsPresentationTicker.Choose(
                ["ABCR", "ABCU"],
                Filed(("ABCR", ListedSecurityType.Rights), ("ABCU", ListedSecurityType.Units))
            )
        );
        Assert.Equal(
            "ABC",
            UsPresentationTicker.Choose(
                ["ABCR", "ABC"],
                Filed(("ABCR", ListedSecurityType.Rights), ("ABC", ListedSecurityType.Other))
            )
        );
    }

    [Fact]
    public void FiledCommonSibling_WinsOverAnEarlierUnclassifiedSibling()
    {
        var chosen = UsPresentationTicker.Choose(
            ["ABCL", "ABC-PB", "ABC"],
            Filed(
                ("ABCL", ListedSecurityType.DebtSecurities),
                ("ABC", ListedSecurityType.CommonShares)
            )
        );

        Assert.Equal("ABC", chosen);
    }

    [Fact]
    public void EveryTickerFiledNonEquity_KeepsSecOrder()
    {
        var chosen = UsPresentationTicker.Choose(
            ["ABCL", "ABC-PA", "ABCW"],
            Filed(
                ("ABCL", ListedSecurityType.DebtSecurities),
                ("ABCPA", ListedSecurityType.PreferredShares),
                ("ABCW", ListedSecurityType.Warrants)
            )
        );

        Assert.Equal("ABCL", chosen);
    }

    [Fact]
    public void FiledUnitsListedFirst_NeverYieldToAnUnclassifiedSibling()
    {
        var chosen = UsPresentationTicker.Choose(
            ["ABCU", "ABCW", "ABC"],
            Filed(("ABCU", ListedSecurityType.Units), ("ABCW", ListedSecurityType.Warrants))
        );

        Assert.Equal("ABCU", chosen);
    }

    [Fact]
    public void OverlongSiblingIsNeverPresentedInPlaceOfDebt()
    {
        var chosen = UsPresentationTicker.Choose(
            ["ABCL", "ABCDEFGHIJKLMNOPQ", "ABC"],
            Filed(("ABCL", ListedSecurityType.DebtSecurities))
        );

        Assert.Equal("ABC", chosen);
    }

    [Fact]
    public void NoRegistrations_KeepSecOrder()
    {
        Assert.Equal("BEATW", UsPresentationTicker.Choose(["BEATW", "BEAT"], null));
    }

    [Fact]
    public void OverlongTickersAreNeverPresented()
    {
        Assert.Equal("ABC", UsPresentationTicker.Choose(["ABCDEFGHIJKLMNOPQ", "ABC"], null));
        Assert.Null(UsPresentationTicker.Choose(["ABCDEFGHIJKLMNOPQ"], null));
    }
}
