using System.ComponentModel.DataAnnotations;
using Equibles.Integrations.DelayedTrades;
using Microsoft.EntityFrameworkCore;

namespace Equibles.DelayedTrades.Data.Models;

// The evidence ledger of every fetch: which file, from where, under which terms, with which hash. Raw files are not kept.
[Index(nameof(FetchedAtUtc))]
[Index(nameof(MarketCode), nameof(FetchedAtUtc))]
public class DelayedTradeFileCapture
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required, MaxLength(32)]
    public string SourceKey { get; set; }

    [Required, MaxLength(8)]
    public string LocationCode { get; set; }

    [Required, MaxLength(32)]
    public string MarketCode { get; set; }

    public DelayedTradeWindow Window { get; set; }
    public DelayedTradeFetchOutcome Outcome { get; set; }

    [Required, MaxLength(500)]
    public string SourceUrl { get; set; }

    [Required, MaxLength(500)]
    public string TermsUrl { get; set; }

    [MaxLength(64)]
    public string Sha256 { get; set; }

    public long Bytes { get; set; }
    public int Rows { get; set; }
    public DateOnly? SessionDate { get; set; }
    public DateTime FetchedAtUtc { get; set; }
}
