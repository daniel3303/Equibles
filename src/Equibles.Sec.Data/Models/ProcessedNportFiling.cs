using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Sec.Data.Models;

/// <summary>
/// Records an NPORT-P submission the daily-index sweep examined and deliberately did NOT store as a
/// <see cref="NportFiling"/>, keyed by accession number. Two cases reach here: the registrant is a
/// tracked stock (its filings are crawled at full fidelity through the issuer feed instead), or the
/// report carries no position in any stock we track and the series has never been seen before.
///
/// This is the dedup ledger that keeps the sweep cheap: without it, every Vanguard bond fund and
/// money-market series in the trailing window would be re-downloaded and re-parsed each cycle.
/// Stored filings need no entry here — their accession already exists in <see cref="NportFiling"/>,
/// which the sweep checks first.
/// </summary>
[Index(nameof(AccessionNumber), IsUnique = true)]
public class ProcessedNportFiling
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    [MaxLength(32)]
    public string AccessionNumber { get; set; }

    public DateTime CreationTime { get; set; } = DateTime.UtcNow;
}
