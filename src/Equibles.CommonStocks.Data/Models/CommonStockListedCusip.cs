using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace Equibles.CommonStocks.Data.Models;

/// <summary>
/// The CUSIP of one of a filer's OTHER listed securities — a sibling share class,
/// unit, or separate fund series named in <see cref="CommonStock.SecondaryTickers"/>.
/// <para>
/// <see cref="CommonStock.Cusip"/> identifies only the primary listing, so 13F lines
/// filed under a sibling class's CUSIP (Alphabet Class C at 02079K107 beside GOOGL's
/// 02079K305) matched nothing and were dropped at import. This table gives each
/// authoritative secondary listing its own CUSIP identity so those positions resolve
/// to the filer row WITHOUT being merged into the primary class: the holdings lane
/// keys the resulting rows by the listed ticker recorded here.
/// </para>
/// <para>
/// Deliberately separate from <see cref="CommonStockCusipAlias"/>: an alias is a
/// retired identity of the PRIMARY security and maps to the primary series, while a
/// row here is the current identity of a DIFFERENT security. Folding these together
/// would re-create the class collapse the table exists to prevent.
/// </para>
/// <para>
/// Rows are recorded only from the SEC's CNS fails feed, where the SEC itself
/// publishes SYMBOL and CUSIP on one row — never from name matching. One CUSIP
/// identifies one security, ever: the unique index keeps the first owner.
/// </para>
/// </summary>
[Index(nameof(Cusip), IsUnique = true)]
[Index(nameof(CommonStockId))]
public class CommonStockListedCusip
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid CommonStockId { get; set; }

    public virtual CommonStock CommonStock { get; set; }

    /// <summary>The exact canonical listed symbol, as spelled in SecondaryTickers (dash form).</summary>
    [Required]
    [MaxLength(32)]
    public string ListedTicker { get; set; }

    [Required]
    [MaxLength(9)]
    public string Cusip { get; set; }

    public DateTime CreationTime { get; set; } = DateTime.UtcNow;
}
