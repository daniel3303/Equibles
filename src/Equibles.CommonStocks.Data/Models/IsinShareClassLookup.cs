using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace Equibles.CommonStocks.Data.Models;

// OpenFIGI's answer for one listed ISIN: the US line of that same share, and whether it is quoted over the
// counter. It is merge evidence only and never joins a security or an issuer.
[Index(nameof(NextAttemptAt))]
public class IsinShareClassLookup
{
    [Key, MaxLength(12)]
    public string Isin { get; set; }

    // The share class of the US line; null when OpenFIGI states no US line or its lines disagree.
    [MaxLength(12)]
    public string ShareClassFigi { get; set; }

    // Every ticker OpenFIGI's US composite states for the ISIN.
    [Required]
    public List<string> UsTickers { get; set; } = [];

    // OpenFIGI's security type of the US line, when its lines agree on one.
    [MaxLength(64)]
    public string UsSecurityType { get; set; }

    // OpenFIGI's over-the-counter venue quotes every US ticker under the same share class; an exchange line never is.
    public bool QuotedOverTheCounter { get; set; }

    public Guid? SourceRecordId { get; set; }
    public virtual EquityDirectorySourceRecord SourceRecord { get; set; }

    public DateTime? CheckedAt { get; set; }
    public DateTime NextAttemptAt { get; set; }

    [MaxLength(2000)]
    public string LastError { get; set; }
}
