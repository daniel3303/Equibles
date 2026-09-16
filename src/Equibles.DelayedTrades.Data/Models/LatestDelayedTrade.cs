using System.ComponentModel.DataAnnotations;
using Equibles.CommonStocks.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.DelayedTrades.Data.Models;

// The newest delayed print of one verified listing plus the session it belongs to; every poll re-derives the
// row from the venue's current-session file and it never regresses to an older session.
[Index(nameof(MarketCode))]
public class LatestDelayedTrade
{
    [Key]
    public Guid EquityListingId { get; set; }
    public virtual EquityListing Listing { get; set; }

    [Required, MaxLength(12)]
    public string Isin { get; set; }

    [Required, MaxLength(4)]
    public string Mic { get; set; }

    // The listing's ISO currency; prices are stored in the listing's quotation unit, display multiplies.
    [Required, MaxLength(3)]
    public string Currency { get; set; }

    [Required, MaxLength(32)]
    public string SourceKey { get; set; }

    [Required, MaxLength(32)]
    public string MarketCode { get; set; }

    public DateOnly SessionDate { get; set; }

    [Precision(18, 4)]
    public decimal LastPrice { get; set; }

    [Precision(18, 4)]
    public decimal LastQuantity { get; set; }

    public DateTime LastTradedAtUtc { get; set; }
    public DateTime LastPublishedAtUtc { get; set; }

    [MaxLength(64)]
    public string LastTradeId { get; set; }

    [Precision(18, 4)]
    public decimal SessionOpen { get; set; }

    [Precision(18, 4)]
    public decimal SessionHigh { get; set; }

    [Precision(18, 4)]
    public decimal SessionLow { get; set; }

    public long Volume { get; set; }
    public long LitVolume { get; set; }
    public int PrintCount { get; set; }
    public bool IsSessionComplete { get; set; }

    [Required, MaxLength(500)]
    public string SourceUrl { get; set; }

    [Required, MaxLength(500)]
    public string TermsUrl { get; set; }

    [MaxLength(64)]
    public string FileSha256 { get; set; }

    public DateTime CapturedAtUtc { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
