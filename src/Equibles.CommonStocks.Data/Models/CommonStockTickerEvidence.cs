using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Equibles.CommonStocks.Data.Models;

/// <summary>
/// One authoritative observation that an SEC filer stated a trading symbol in a filing's
/// cover-page 12(b) table. Unlike <see cref="CommonStockTickerAlias"/>, evidence is never
/// reassigned or collapsed when an exchange later reuses the symbol for another issuer.
/// </summary>
[Index(nameof(Ticker), nameof(FiledDate))]
[Index(nameof(CommonStockId), nameof(Ticker), nameof(SourceDocumentId), IsUnique = true)]
public class CommonStockTickerEvidence
{
    // The XBRL extractor version that first preserves this evidence. Congress replay must not
    // activate until the processable captured corpus reaches this version.
    public const int SourceXbrlFactsVersion = 5;

    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid CommonStockId { get; set; }
    public virtual CommonStock CommonStock { get; set; }

    [Required]
    [MaxLength(32)]
    public string Ticker { get; set; }

    public DateOnly FiledDate { get; set; }

    /// <summary>
    /// Immutable identity of the SEC document that supplied the observation. Kept as a scalar
    /// rather than a navigation because CommonStocks.Data must not depend on Sec.Data.
    /// </summary>
    public Guid SourceDocumentId { get; set; }

    [Required]
    [MaxLength(32)]
    public string AccessionNumber { get; set; }
}
