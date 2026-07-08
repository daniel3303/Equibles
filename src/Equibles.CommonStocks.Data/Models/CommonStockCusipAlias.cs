using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace Equibles.CommonStocks.Data.Models;

/// <summary>
/// A CUSIP that previously identified the stock's listed share class. Corporate
/// actions (share-class conversions, reincorporations, name changes) assign the
/// security a new CUSIP; filings keep referencing the retired one — laggard 13F
/// filers for a quarter or two, and every historical data set forever — so
/// import-time CUSIP resolution maps the union of the current CUSIP and these
/// aliases to the stock. Rows are recorded by
/// <c>CommonStockManager.SetCusip</c> whenever a non-null CUSIP changes.
/// </summary>
[Index(nameof(Cusip), IsUnique = true)]
[Index(nameof(CommonStockId))]
public class CommonStockCusipAlias
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid CommonStockId { get; set; }

    public virtual CommonStock CommonStock { get; set; }

    [Required]
    [MaxLength(9)]
    public string Cusip { get; set; }

    public DateTime CreationTime { get; set; } = DateTime.UtcNow;
}
