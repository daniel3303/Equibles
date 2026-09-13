using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Equibles.CommonStocks.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.CorporateActions.Data.Models;

/// <summary>
/// An as-reported cash dividend for a <see cref="Issuer"/>.
/// <see cref="ExDate"/> is the ex-dividend date (the first trading day the
/// stock trades without the dividend) and <see cref="AmountPerShare"/> is the
/// declared cash amount in major currency units per share. Attributed observations are unique
/// per listing and ex-date; earlier issuer-only observations retain their separate identity.
/// </summary>
[Index(nameof(PriceAdjustmentAppliedTime))]
public class CashDividend
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid EquityIssuerId { get; set; }
    public virtual EquityIssuer Issuer { get; set; }

    // Earlier captures did not retain the source listing or denomination.
    public Guid? EquityListingId { get; set; }
    public virtual EquityListing Listing { get; set; }

    [MaxLength(3)]
    public string Currency { get; set; }

    public DateOnly ExDate { get; set; }

    public decimal AmountPerShare { get; set; }

    public CashDividendSource Source { get; set; }

    public DateTime CreationTime { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// The amount incorporated into the last full price-history reconciliation. Keeping the
    /// applied value makes a restatement by an older worker detectable even when that worker does
    /// not know to clear <see cref="PriceAdjustmentAppliedTime"/>.
    /// </summary>
    public decimal? PriceAdjustmentAppliedAmountPerShare { get; set; }

    /// <summary>
    /// Null while this dividend still requires a full provider-history reconciliation of the
    /// captured listing's price series.
    /// </summary>
    public DateTime? PriceAdjustmentAppliedTime { get; set; }
}
