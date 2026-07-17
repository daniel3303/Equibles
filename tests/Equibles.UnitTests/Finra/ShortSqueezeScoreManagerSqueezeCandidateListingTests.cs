using Equibles.CommonStocks.Data.Models;
using Equibles.Finra.BusinessLogic;

namespace Equibles.UnitTests.Finra;

/// <summary>
/// Pins the scored-universe listing gate: the unambiguously non-equity kinds are out,
/// Units stay in (MLP common units are genuine operating equity), and the single
/// Units carve-out is the SIC-6221 commodity/currency trust — units created and
/// redeemed at NAV can never be squeezed, so a CurrencyShares-style trust must not
/// rank on the board while an MLP with the same 12(b) "units" title must.
/// </summary>
public class ShortSqueezeScoreManagerSqueezeCandidateListingTests
{
    private static bool IsCandidate(ListedSecurityType type, string sic) =>
        ShortSqueezeScoreManager.SqueezeCandidateListing.Compile()(
            new CommonStock { ListedSecurityType = type, Sic = sic }
        );

    [Theory]
    [InlineData(ListedSecurityType.CommonShares, "3674")]
    [InlineData(ListedSecurityType.Unknown, "3674")]
    [InlineData(ListedSecurityType.Other, "3674")]
    // MLP common units keep their industry SIC (pipelines, oil & gas, coal) — in.
    [InlineData(ListedSecurityType.Units, "4922")]
    [InlineData(ListedSecurityType.Units, "1311")]
    // A COMMON-shares listing of a SIC-6221 broker is not a trust — the carve-out
    // requires both signals.
    [InlineData(ListedSecurityType.CommonShares, "6221")]
    public void SqueezeCandidateListing_EquityAndMlpUnits_AreCandidates(
        ListedSecurityType type,
        string sic
    )
    {
        IsCandidate(type, sic).Should().BeTrue();
    }

    [Theory]
    [InlineData(ListedSecurityType.PreferredShares, "3674")]
    [InlineData(ListedSecurityType.DebtSecurities, "3674")]
    [InlineData(ListedSecurityType.Warrants, "3674")]
    [InlineData(ListedSecurityType.Rights, "3674")]
    // The FXC shape: a currency/commodity trust registers "units" on its 12(b)
    // cover and carries SIC 6221 — excluded on that authoritative pair.
    [InlineData(ListedSecurityType.Units, "6221")]
    public void SqueezeCandidateListing_NonEquityAndCommodityTrustUnits_AreExcluded(
        ListedSecurityType type,
        string sic
    )
    {
        IsCandidate(type, sic).Should().BeFalse();
    }
}
