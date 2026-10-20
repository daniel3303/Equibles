using Equibles.Sec.Data.Models;

namespace Equibles.Holdings.BusinessLogic.Advisers;

/// <summary>One fund type on a Schedule D 7.B.(1) filing: its funds and their gross asset value.</summary>
public readonly record struct ScheduleDFundType(
    FormAdvPrivateFundType Type,
    int Count,
    long GrossAssets
)
{
    /// <summary>The filing's fund types that list at least one fund, largest gross assets first.</summary>
    public static List<ScheduleDFundType> Of(FormAdvPrivateFundFiling filing) =>
        new ScheduleDFundType[]
        {
            new(FormAdvPrivateFundType.Hedge, filing.HedgeFundCount, filing.HedgeFundGrossAssets),
            new(
                FormAdvPrivateFundType.PrivateEquity,
                filing.PrivateEquityFundCount,
                filing.PrivateEquityFundGrossAssets
            ),
            new(
                FormAdvPrivateFundType.VentureCapital,
                filing.VentureCapitalFundCount,
                filing.VentureCapitalFundGrossAssets
            ),
            new(
                FormAdvPrivateFundType.RealEstate,
                filing.RealEstateFundCount,
                filing.RealEstateFundGrossAssets
            ),
            new(
                FormAdvPrivateFundType.Liquidity,
                filing.LiquidityFundCount,
                filing.LiquidityFundGrossAssets
            ),
            new(
                FormAdvPrivateFundType.Securitized,
                filing.SecuritizedAssetFundCount,
                filing.SecuritizedAssetFundGrossAssets
            ),
            new(
                FormAdvPrivateFundType.Other,
                filing.OtherPrivateFundCount,
                filing.OtherPrivateFundGrossAssets
            ),
        }
            .Where(type => type.Count > 0)
            .OrderByDescending(type => type.GrossAssets)
            .ThenBy(type => type.Type)
            .ToList();
}
