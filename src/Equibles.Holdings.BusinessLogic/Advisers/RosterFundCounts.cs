using Equibles.Sec.Data.Models;

namespace Equibles.Holdings.BusinessLogic.Advisers;

/// <summary>An adviser's private fund counts by type as the monthly Form ADV roster lists them.</summary>
public readonly record struct RosterFundCounts(
    int? Hedge,
    int? PrivateEquity,
    int? VentureCapital,
    int? RealEstate,
    int? Liquidity,
    int? Securitized,
    int? Other
)
{
    public static RosterFundCounts Of(FormAdvAdviser adviser) =>
        new(
            adviser.HedgeFundCount,
            adviser.PrivateEquityFundCount,
            adviser.VentureCapitalFundCount,
            adviser.RealEstateFundCount,
            adviser.LiquidityFundCount,
            adviser.SecuritizedAssetFundCount,
            adviser.OtherPrivateFundCount
        );
}
