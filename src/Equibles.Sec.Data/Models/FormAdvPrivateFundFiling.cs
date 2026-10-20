using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Sec.Data.Models;

/// <summary>
/// The private funds one Form ADV filing lists on Schedule D 7.B.(1), counted and summed by fund
/// type, from SEC's Form ADV filing archive. SEC's bulk archive ends with filings submitted on
/// 2024-12-31, so every row is a dated snapshot, never a current figure. A filing is kept only
/// when its sums differ from the adviser's previous kept filing; a kept filing with no funds
/// records that the adviser stopped listing them.
/// </summary>
[Index(nameof(Registration), nameof(FilingId), IsUnique = true)]
[Index(nameof(Crd), nameof(FiledOn))]
public class FormAdvPrivateFundFiling
{
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Organization CRD number of the adviser.</summary>
    public int Crd { get; set; }

    /// <summary>Whether the filing came from the registered or the exempt reporting adviser archive.</summary>
    public FormAdvRegistration Registration { get; set; }

    /// <summary>SEC's identifier of the Form ADV filing.</summary>
    public int FilingId { get; set; }

    /// <summary>The date SEC received the filing, which is the as-of date of every figure.</summary>
    public DateOnly FiledOn { get; set; }

    public int FundCount { get; set; }

    /// <summary>
    /// Sum of the funds' gross asset values (Schedule D 7.B.(1) Question 11), in US dollars; a
    /// blank value or one no private fund could hold (a keying error) adds nothing.
    /// </summary>
    public long GrossAssets { get; set; }

    public int HedgeFundCount { get; set; }
    public long HedgeFundGrossAssets { get; set; }

    public int PrivateEquityFundCount { get; set; }
    public long PrivateEquityFundGrossAssets { get; set; }

    public int VentureCapitalFundCount { get; set; }
    public long VentureCapitalFundGrossAssets { get; set; }

    public int RealEstateFundCount { get; set; }
    public long RealEstateFundGrossAssets { get; set; }

    public int LiquidityFundCount { get; set; }
    public long LiquidityFundGrossAssets { get; set; }

    public int SecuritizedAssetFundCount { get; set; }
    public long SecuritizedAssetFundGrossAssets { get; set; }

    public int OtherPrivateFundCount { get; set; }
    public long OtherPrivateFundGrossAssets { get; set; }
}
