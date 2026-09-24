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
