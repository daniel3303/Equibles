using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace Equibles.CommonStocks.Data.Models;

// GLEIF's answer for one listed ISIN. It is merge evidence only and never assigns an issuer's LEI.
[Index(nameof(NextAttemptAt))]
[Index(nameof(LegalEntityIdentifier))]
public class IsinLegalEntityLookup
{
    [Key, MaxLength(12)]
    public string Isin { get; set; }

    [MaxLength(20)]
    public string LegalEntityIdentifier { get; set; }

    [Required]
    public List<string> RelatedIsins { get; set; } = [];

    // GLEIF's reported total; RelatedIsins is complete only when this is within the client's bound.
    public int RelatedIsinCount { get; set; }

    public Guid? SourceRecordId { get; set; }
    public virtual EquityDirectorySourceRecord SourceRecord { get; set; }

    public DateTime? CheckedAt { get; set; }
    public DateTime NextAttemptAt { get; set; }

    [MaxLength(2000)]
    public string LastError { get; set; }
}
