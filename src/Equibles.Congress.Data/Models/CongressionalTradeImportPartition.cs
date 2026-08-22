using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Congress.Data.Models;

/// <summary>
/// Marks one chamber/year archive window complete for a parser generation. The worker uses these
/// durable partitions to backfill one year per cycle without repeatedly searching the whole STOCK
/// Act archive; a newer parser version reopens the row for replay.
/// </summary>
[Index(nameof(Kind), nameof(Year), IsUnique = true)]
public class CongressionalTradeImportPartition
{
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public Guid Id { get; set; } = Guid.NewGuid();

    public CongressionalFilingKind Kind { get; set; }
    public int Year { get; set; }
    public int ParserVersion { get; set; }
    public int FilingCount { get; set; }
    public int TransactionCount { get; set; }
    public DateTime CompletionTime { get; set; } = DateTime.UtcNow;
}
