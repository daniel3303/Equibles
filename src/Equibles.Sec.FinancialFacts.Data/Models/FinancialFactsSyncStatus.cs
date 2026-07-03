using System.ComponentModel.DataAnnotations.Schema;
using Equibles.CommonStocks.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Sec.FinancialFacts.Data.Models;

/// <summary>
/// Per-company ingestion checkpoint. Lets the scraper skip companies whose
/// Company Facts have not changed since the last successful sync.
/// </summary>
[Index(nameof(CommonStockId), IsUnique = true)]
public class FinancialFactsSyncStatus
{
    // Client-generated Guid key. Without DatabaseGeneratedOption.None EF marks
    // it store-generated, so FlexLabs UpsertRange omits it from the INSERT and
    // Postgres (no column default) rejects the row with a NOT NULL violation.
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid CommonStockId { get; set; }
    public virtual CommonStock CommonStock { get; set; }

    public DateTime LastCheckedAt { get; set; }

    /// <summary>Newest filed date ingested so far; null until the first run.</summary>
    public DateOnly? LastFiledDateSeen { get; set; }

    /// <summary>
    /// When the concept-metadata sweep (labels, descriptions, balance from the
    /// company's recent filings' MetaLinks) last ran for this company; null
    /// until the first run. A <see cref="LastFiledDateSeen"/> newer than this
    /// marks the company due again — a new filing can introduce new concepts.
    /// </summary>
    public DateTime? ConceptMetadataCheckedAt { get; set; }
}
