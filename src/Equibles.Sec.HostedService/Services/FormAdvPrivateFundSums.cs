using Equibles.Sec.Data.Models;

namespace Equibles.Sec.HostedService.Services;

/// <summary>
/// One filing's Schedule D 7.B.(1) funds counted and summed by type. A fund with no recognized
/// type counts toward the totals only.
/// </summary>
internal sealed class FormAdvPrivateFundSums
{
    // Declared before None, which static initialization builds in textual order.
    private static readonly int TypeCount = Enum.GetValues<FormAdvPrivateFundType>().Length;

    internal static readonly FormAdvPrivateFundSums None = new();

    private readonly int[] _counts = new int[TypeCount];
    private readonly long[] _assets = new long[TypeCount];

    internal int FundCount { get; private set; }
    internal long GrossAssets { get; private set; }

    internal void Add(FormAdvPrivateFundType? type, long grossAssets)
    {
        FundCount++;
        GrossAssets += grossAssets;
        if (type is not { } known)
            return;
        _counts[(int)known]++;
        _assets[(int)known] += grossAssets;
    }

    internal int Count(FormAdvPrivateFundType type) => _counts[(int)type];

    internal long Assets(FormAdvPrivateFundType type) => _assets[(int)type];

    internal bool SameAs(FormAdvPrivateFundSums other) =>
        FundCount == other.FundCount
        && GrossAssets == other.GrossAssets
        && _counts.AsSpan().SequenceEqual(other._counts)
        && _assets.AsSpan().SequenceEqual(other._assets);

    internal FormAdvPrivateFundFiling ToFiling(
        FormAdvFilingRow filing,
        FormAdvRegistration registration
    ) =>
        new()
        {
            Crd = filing.Crd,
            Registration = registration,
            FilingId = filing.FilingId,
            FiledOn = filing.FiledOn,
            FundCount = FundCount,
            GrossAssets = GrossAssets,
            HedgeFundCount = Count(FormAdvPrivateFundType.Hedge),
            HedgeFundGrossAssets = Assets(FormAdvPrivateFundType.Hedge),
            PrivateEquityFundCount = Count(FormAdvPrivateFundType.PrivateEquity),
            PrivateEquityFundGrossAssets = Assets(FormAdvPrivateFundType.PrivateEquity),
            VentureCapitalFundCount = Count(FormAdvPrivateFundType.VentureCapital),
            VentureCapitalFundGrossAssets = Assets(FormAdvPrivateFundType.VentureCapital),
            RealEstateFundCount = Count(FormAdvPrivateFundType.RealEstate),
            RealEstateFundGrossAssets = Assets(FormAdvPrivateFundType.RealEstate),
            LiquidityFundCount = Count(FormAdvPrivateFundType.Liquidity),
            LiquidityFundGrossAssets = Assets(FormAdvPrivateFundType.Liquidity),
            SecuritizedAssetFundCount = Count(FormAdvPrivateFundType.Securitized),
            SecuritizedAssetFundGrossAssets = Assets(FormAdvPrivateFundType.Securitized),
            OtherPrivateFundCount = Count(FormAdvPrivateFundType.Other),
            OtherPrivateFundGrossAssets = Assets(FormAdvPrivateFundType.Other),
        };
}
