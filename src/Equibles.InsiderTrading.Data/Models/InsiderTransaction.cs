using System.ComponentModel.DataAnnotations;
using Equibles.CommonStocks.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.InsiderTrading.Data.Models;

[Index(nameof(CommonStockId), nameof(TransactionDate))]
[Index(nameof(InsiderOwnerId), nameof(TransactionDate))]
[Index(nameof(AccessionNumber), nameof(TransactionOrder), IsUnique = true)]
[Index(nameof(FilingDate))]
[Index(nameof(TransactionDate))]
[Index(nameof(IsPriceValid), nameof(TransactionDate))]
public class InsiderTransaction
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid InsiderOwnerId { get; set; }
    public virtual InsiderOwner InsiderOwner { get; set; }

    public Guid CommonStockId { get; set; }
    public virtual CommonStock CommonStock { get; set; }

    public DateOnly FilingDate { get; set; }
    public DateOnly TransactionDate { get; set; }

    public TransactionCode TransactionCode { get; set; }

    public long Shares { get; set; }

    /// <summary>
    /// Effective per-share price used by dashboards and sorts. Equals
    /// <see cref="ReportedPricePerShare"/> for normal rows; diverges only
    /// when a fat-fingered filing was repaired — see <see cref="IsPriceValid"/>.
    /// </summary>
    public decimal PricePerShare { get; set; }

    /// <summary>
    /// The per-share price exactly as filed, before any repair. Always
    /// populated, so the raw filing value is never lost. When a filer types
    /// the total transaction value into the per-share field, the repair
    /// recovers the unit price as <c>ReportedPricePerShare / Shares</c> and
    /// stores it in <see cref="PricePerShare"/>, leaving this column holding
    /// the original (bad) value for audit and reversibility.
    /// </summary>
    public decimal ReportedPricePerShare { get; set; }

    public AcquiredDisposed AcquiredDisposed { get; set; }
    public long SharesOwnedAfter { get; set; }
    public OwnershipNature OwnershipNature { get; set; }

    [MaxLength(128)]
    public string SecurityTitle { get; set; }

    [MaxLength(32)]
    public string AccessionNumber { get; set; }

    /// <summary>
    /// Ordinal position of this row within its Form 3/4 filing (0-based). Form 4 XML
    /// has no per-transaction identifier — uniqueness is by (AccessionNumber, position).
    /// </summary>
    public int TransactionOrder { get; set; }

    public bool IsAmendment { get; set; }

    /// <summary>
    /// Tri-state price-plausibility flag, cross-checked against the Yahoo
    /// unadjusted close on TransactionDate:
    /// <list type="bullet">
    /// <item><c>null</c> — not evaluated yet (no close available when the row
    /// was ingested); a later maintenance recompute re-checks it once the
    /// close exists.</item>
    /// <item><c>true</c> — plausible, or repaired (see
    /// <see cref="ReportedPricePerShare"/>).</item>
    /// <item><c>false</c> — implausible and not repairable (<see cref="Shares"/>
    /// is 0, so the mis-entered total can't be divided back to a unit price).</item>
    /// </list>
    /// Dashboards show a row unless this is positively <c>false</c>, so a
    /// single fat-fingered Form 4 doesn't drown out every other row when
    /// sorted by Shares × PricePerShare.
    /// </summary>
    public bool? IsPriceValid { get; set; }

    public DateTime CreationTime { get; set; } = DateTime.UtcNow;
}
