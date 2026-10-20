using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Holdings.Data.Models;

/// <summary>
/// One of a listed security's closest holder-overlap peers at a closed 13F quarter: the securities
/// most often found among the same active filers' hundred largest positions, by cosine similarity
/// of their holder sets. Only filers measured concentrated or diversified active count, since an
/// index-like or systematic book holds everything together.
/// </summary>
[Index(nameof(ReportDate))]
public class StockQuarterlyHolderOverlap
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid EquityIssuerId { get; set; }

    [MaxLength(32)]
    public string ListedTicker { get; set; }

    public DateOnly ReportDate { get; set; }

    /// <summary>1 for the closest peer.</summary>
    public int Rank { get; set; }

    public Guid PeerEquityIssuerId { get; set; }

    [MaxLength(32)]
    public string PeerListedTicker { get; set; }

    /// <summary>Active filers holding both among their hundred largest positions.</summary>
    public int SharedHolders { get; set; }

    /// <summary>Shared holders over the geometric mean of each side's holders, from 0 to 1.</summary>
    [Precision(8, 6)]
    public decimal Similarity { get; set; }
}
