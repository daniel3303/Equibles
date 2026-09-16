using System.ComponentModel.DataAnnotations;

namespace Equibles.EquityMarkets.Data.Models;

// The pass state of one catalog market: refresh requests, the last directory counts and the last error.
public class EquityMarketRegistration
{
    [Key, MaxLength(32)]
    public string Code { get; set; }

    // Retired: nothing reads it since the per-market switch went away; a later migration drops the column.
    public bool Enabled { get; set; }

    public DateTime? DirectoryRefreshRequestedAt { get; set; }
    public DateTime? DirectoryRefreshedAt { get; set; }
    public int DirectoryListingCount { get; set; }
    public int DirectoryImportedCount { get; set; }
    public int DirectoryCurrentCount { get; set; }
    public int DirectorySkippedCount { get; set; }
    public int DirectoryFailedCount { get; set; }

    [MaxLength(1000)]
    public string LastError { get; set; }

    // Delayed-trades pass state: the last served fetch, the last settled session and the last fault.
    public DateTime? DelayedTradesCapturedAt { get; set; }
    public DateOnly? DelayedTradesSessionDate { get; set; }

    [MaxLength(1000)]
    public string DelayedTradesLastError { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
