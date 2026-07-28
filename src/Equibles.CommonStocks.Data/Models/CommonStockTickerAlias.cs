using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace Equibles.CommonStocks.Data.Models;

/// <summary>
/// A primary ticker the stock previously traded under. The SEC sync renames
/// <c>CommonStock.Ticker</c> in place when an issuer changes its symbol
/// (LendingClub's LC → HAPN), which orphans every URL, bookmark and search
/// result published under the old symbol — the row's other identifiers survive
/// the rename, but the old ticker itself is gone from the table. Rows are
/// recorded by <c>CommonStockManager.RecordTickerAlias</c> whenever the sync
/// changes a primary ticker, so a miss-path lookup can 301 the old symbol to
/// the current one.
///
/// This is deliberately a separate map rather than a history column on the
/// stock: it is consulted only on the miss path, so no ticker query — screener,
/// sitemap, importer maps, uniqueness validation — can accidentally see a
/// retired symbol. Unlike the CUSIP alias (first owner keeps a retired CUSIP
/// forever), ticker aliases are last-writer-wins: exchanges recycle symbols
/// across unrelated issuers, so the most recent holder of a symbol owns its
/// redirect, and an alias is deleted outright when a company adopts that
/// symbol as a live ticker again.
/// </summary>
[Index(nameof(Ticker), IsUnique = true)]
[Index(nameof(CommonStockId))]
public class CommonStockTickerAlias
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid CommonStockId { get; set; }

    public virtual CommonStock CommonStock { get; set; }

    [Required]
    [MaxLength(16)]
    public string Ticker { get; set; }

    public DateTime CreationTime { get; set; } = DateTime.UtcNow;
}
