using System.ComponentModel.DataAnnotations;

namespace Equibles.EquityMarkets.Data.Models;

// The operator switch for one catalog market; workers read it every cycle, so no host setting decides a lane.
public class EquityMarketRegistration
{
    [Key, MaxLength(32)]
    public string Code { get; set; }

    public bool Enabled { get; set; }
    public bool DelayedTradesEnabled { get; set; }

    public DateTime? DirectoryRefreshRequestedAt { get; set; }
    public DateTime? DirectoryRefreshedAt { get; set; }
    public int DirectoryListingCount { get; set; }
    public int DirectoryImportedCount { get; set; }
    public int DirectorySkippedCount { get; set; }
    public int DirectoryFailedCount { get; set; }
    public DateTime? DelayedTradesRefreshedAt { get; set; }

    [MaxLength(1000)]
    public string LastError { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
